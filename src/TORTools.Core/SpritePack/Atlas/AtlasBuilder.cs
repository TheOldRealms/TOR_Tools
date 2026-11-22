using System.IO.Hashing;
using TORTools.Core.SpritePack.Rdc;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// End-to-end sprite-atlas build pipeline. Consumes a module folder
/// shaped like a Bannerlord module (<c>GUI/SpriteParts/&lt;cat&gt;/*.png</c>
/// + <c>GUI/SpriteParts/Config.xml</c>) and emits:
///
/// <list type="bullet">
///   <item><c>&lt;Module&gt;/AssetSources/GauntletUI/&lt;sheet&gt;.png</c></item>
///   <item><c>&lt;Module&gt;/GUI/&lt;Module&gt;SpriteData.xml</c></item>
///   <item><c>&lt;Module&gt;/AssetPackages/&lt;Module&gt;_ui.tpac</c> (or per-category)</item>
///   <item><c>&lt;Module&gt;/RuntimeDataCache/&lt;pkg-guid&gt;.rdc</c></item>
/// </list>
///
/// Mirrors the Modding Kit's "Import Resources" output: textures carry
/// TEX_SETTINGS + TEX_SRCINFO data segments in the TPAC and the pixel
/// payload lives in the RDC.
/// </summary>
public static class AtlasBuilder
{
    public static BuildReport Build(string moduleDir, AtlasOptions? options = null)
    {
        options ??= new AtlasOptions();
        moduleDir = Path.GetFullPath(moduleDir);
        var moduleName = Path.GetFileName(moduleDir.TrimEnd(Path.DirectorySeparatorChar));

        var report = new BuildReport { Module = moduleName, ModuleDir = moduleDir };

        // 1. Scan + parse config
        var partsRoot = Path.Combine(moduleDir, "GUI", "SpriteParts");
        var paths = SpriteScanner.ScanPngs(partsRoot);
        if (paths.Count == 0)
            throw new InvalidDataException($"{partsRoot}: no PNG files found.");

        var configs = LoadConfigs(moduleDir, report.Warnings);

        // 2. Group PNGs by category, build SpritePart instances in scan order
        var categories = BuildCategories(partsRoot, paths, configs);
        report.Categories.AddRange(categories.Select(c => c.Name));

        // 3. Bin-pack + rendering + encoding per category
        var sheetOutDir = Path.Combine(moduleDir, "AssetSources", "GauntletUI");
        if (options.WriteSheets) report.SheetDir = sheetOutDir;

        var textures = new List<TextureBuildResult>();
        foreach (var category in categories)
        {
            var sheets = category.Config.PackAllSpritesToUniqueTextures
                ? BinPacker.PackUnique(category)
                : BinPacker.PackNormal(category, options.SheetSize, options.SheetSize,
                    category.Config.HasEdge ? category.Config.EdgeSize : options.EdgeSize);
            foreach (var sheet in sheets) BinPacker.RecalculateSize(sheet);

            category.Sheets.AddRange(sheets);
            category.SpriteSheetCount = sheets.Count;

            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                sheet.Id = i + 1;
                var sheetName = $"{category.Name}_{i + 1}";

                var pngPath = Path.Combine(sheetOutDir, sheetName + ".png");
                var rgba = options.WriteSheets
                    ? AtlasRenderer.Render(sheet, pngPath)
                    : AtlasRenderer.Render(sheet, Path.Combine(Path.GetTempPath(), sheetName + ".png"));

                var useDxt5 = options.Format switch
                {
                    FormatMode.Rgba8 => false,
                    FormatMode.Dxt5 => true,
                    _ => category.Config.Compress,
                };

                var (payload, formatString) = useDxt5
                    ? (BcEncoder.EncodeDxt5(rgba, sheet.Width, sheet.Height), "DXT5")
                    : (BcEncoder.EncodeRgba8(rgba), "R8G8B8A8_UNORM");

                report.Sheets.Add(new SheetInfo
                {
                    Name = sheetName,
                    Category = category.Name,
                    Width = sheet.Width,
                    Height = sheet.Height,
                    Format = formatString,
                });

                textures.Add(new TextureBuildResult(
                    SheetName:    sheetName,
                    Category:     category,
                    Sheet:        sheet,
                    SourcePngPath: pngPath,
                    EncodedPayload: payload,
                    Format:       formatString,
                    HasAlpha:     HasAlpha(rgba)));
            }
        }

        // 4. SpriteData.xml
        var allParts = categories.SelectMany(c => c.Parts).ToList();
        var allSprites = allParts.Select(p => new SpriteEntry { Name = p.Name, Part = p }).ToList();
        var xmlBytes = SpriteDataXmlWriter.Build(categories, allParts, allSprites);
        var xmlPath = Path.Combine(moduleDir, "GUI", moduleName + "SpriteData.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
        File.WriteAllBytes(xmlPath, xmlBytes);
        report.XmlPath = xmlPath;

        // 5. TPAC + RDC
        if (options.WriteTpac) WriteTpacsAndRdc(textures, moduleName, moduleDir, options, report);

        return report;
    }

    static Dictionary<string, CategoryConfig> LoadConfigs(string moduleDir, List<string> warnings)
    {
        var primary  = Path.Combine(moduleDir, "GUI", "SpriteParts", "Config.xml");
        var fallback = Path.Combine(moduleDir, "GUI", "Config.xml");

        var cfgs = new Dictionary<string, CategoryConfig>(StringComparer.Ordinal);
        foreach (var (path, isFallback) in new[] { (primary, false), (fallback, true) })
        {
            if (!File.Exists(path)) continue;
            if (isFallback)
                warnings.Add($"{path}: Config.xml should be in GUI/SpriteParts/, read anyway");

            try
            {
                var parsed = ConfigParser.ParseFile(path);
                foreach (var (name, cfg) in parsed)
                    if (!cfgs.ContainsKey(name)) cfgs[name] = cfg;
            }
            catch (Exception e)
            {
                warnings.Add($"{path}: failed to load configuration ({e.Message}), using defaults");
            }
        }
        return cfgs;
    }

    static List<SpriteCategory> BuildCategories(string partsRoot,
        IReadOnlyList<string> paths, Dictionary<string, CategoryConfig> configs)
    {
        var byCategory = new Dictionary<string, SpriteCategory>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var relPath in paths)
        {
            var parts = relPath.Split(Path.DirectorySeparatorChar);
            if (parts.Length < 2) continue; // skip stray top-level files
            var categoryName = parts[0];
            var pureName = Path.GetFileNameWithoutExtension(string.Join('\\', parts[1..]));

            if (!byCategory.TryGetValue(categoryName, out var category))
            {
                if (!configs.TryGetValue(categoryName, out var cfg)) cfg = new CategoryConfig();
                category = new SpriteCategory { Name = categoryName, Config = cfg };
                byCategory[categoryName] = category;
                order.Add(categoryName);
            }

            var absolutePath = Path.Combine(partsRoot, relPath);
            var (w, h) = SpriteScanner.GetPngSize(absolutePath);
            category.Parts.Add(new SpritePart
            {
                Name = pureName,
                Category = categoryName,
                Path = absolutePath,
                Width = w,
                Height = h,
            });
        }
        return order.Select(name => byCategory[name]).ToList();
    }

    static void WriteTpacsAndRdc(List<TextureBuildResult> textures,
        string moduleName, string moduleDir, AtlasOptions options, BuildReport report)
    {
        var tpacDir = Path.Combine(moduleDir, "AssetPackages");
        Directory.CreateDirectory(tpacDir);

        var groups = options.PerCategory
            ? textures.GroupBy(t => t.Category.Name).Select(g => (Key: $"{moduleName}_{g.Key}", List: g.ToList())).ToList()
            : new List<(string Key, List<TextureBuildResult> List)>
                { (options.OutName ?? $"{moduleName}_ui", textures) };

        var rdcEntries = new List<RdcEntry>();

        foreach (var group in groups)
        {
            var tpac = new TpacFile
            {
                Version = 2,
                PackageGuid = SpriteGuids.FixedPackageGuid, // one fixed per-package GUID
            };

            foreach (var tex in group.List)
            {
                var assetGuid = SpriteGuids.AssetGuid(moduleName, tex.SheetName);
                var settingsBlob = TextureDataBlobs.BuildSettings("albedo", doNotCompress: true, noMips: false);
                var srcInfoBlob = TextureDataBlobs.BuildSrcInfo((uint)tex.Sheet.Width, (uint)tex.Sheet.Height,
                    "B8G8R8A8_UNORM", (ulong)new FileInfo(tex.SourcePngPath).Length);

                var settingsSeg = new DataSegment
                {
                    TypeGuid = TpacGuids.TexSettings,
                    OwnerGuid = assetGuid,
                    UnknownUint = 4, // observed in Armory
                };
                SegmentPayload.Encode(settingsSeg, settingsBlob);

                var srcInfoSeg = new DataSegment
                {
                    TypeGuid = TpacGuids.TexSrcInfo,
                    OwnerGuid = assetGuid,
                };
                SegmentPayload.Encode(srcInfoSeg, srcInfoBlob);

                var sourceHash = XxHash64.HashToUInt64(File.ReadAllBytes(tex.SourcePngPath));

                var texture = new TextureMetadata
                {
                    MetadataVersion = 3,
                    Source = $"$BASE/Modules/{moduleName}/AssetSources/GauntletUI/{tex.SheetName}.png",
                    SourceHash = sourceHash,
                    Flags = new List<string>(options.TextureFlags),
                    UnknownUint3 = 1,
                    UnknownByte = 2,
                    Width = (uint)tex.Sheet.Width,
                    Height = (uint)tex.Sheet.Height,
                    UnknownUint4 = 1,
                    MipmapCount = 1,
                    ArrayCount = 1,
                    Format = tex.Format,
                    SystemFlags = tex.HasAlpha ? new List<string> { "has_alpha" } : new List<string>(),
                    UnknownUint6 = 4,
                    UnknownUint7 = 1701736302, // = "none" bytes
                    UnknownUlong2 = XxHash64.HashToUInt64(
                        System.Text.Encoding.UTF8.GetBytes($"{moduleName}/{tex.SheetName}")),
                    TrailingMetadata = new byte[] { 0, 0, 0, 1, 0, 0, 0, 1 },
                };

                var asset = new TpacAsset
                {
                    TypeGuid = TpacGuids.Texture,
                    Guid = assetGuid,
                    Name = tex.SheetName,
                    Texture = texture,
                    MetadataChecksum = (long)XxHash64.HashToUInt64(
                        System.Text.Encoding.UTF8.GetBytes(tex.SheetName)),
                };
                asset.Segments.Add(settingsSeg);
                asset.Segments.Add(srcInfoSeg);
                tpac.Assets.Add(asset);

                if (options.WriteRdc)
                {
                    rdcEntries.Add(new RdcEntry
                    {
                        OwnerGuid = assetGuid,
                        DataId = assetGuid,
                        TypeGuid = TpacGuids.TexPixels,
                        Blob = tex.EncodedPayload,
                        Compress = true,
                        Hashes = new ulong[RdcEntry.HashSlots]
                        {
                            sourceHash, XxHash64.HashToUInt64(settingsBlob),
                            0, 0, 0, 0, 0, 0,
                        },
                    });
                }
            }

            var tpacPath = Path.Combine(tpacDir, group.Key + ".tpac");
            TpacWriter.Write(tpacPath, tpac);
            report.TpacPaths.Add(tpacPath);
            report.TpacSizes.Add(new FileInfo(tpacPath).Length);
        }

        if (options.WriteRdc && rdcEntries.Count > 0)
        {
            var rdcFile = new RdcFile();
            rdcFile.Entries.AddRange(rdcEntries);
            var rdcDir = Path.Combine(moduleDir, "RuntimeDataCache");
            Directory.CreateDirectory(rdcDir);
            var rdcPath = Path.Combine(rdcDir, SpriteGuids.FixedPackageGuid.ToString("D").ToUpperInvariant() + ".rdc");
            RdcWriter.Write(rdcPath, rdcFile);
            report.RdcPaths.Add(rdcPath);
        }
    }

    static bool HasAlpha(byte[] rgba8)
    {
        for (var i = 3; i < rgba8.Length; i += 4)
            if (rgba8[i] != 0xFF) return true;
        return false;
    }

    sealed record TextureBuildResult(
        string SheetName,
        SpriteCategory Category,
        Sheet Sheet,
        string SourcePngPath,
        byte[] EncodedPayload,
        string Format,
        bool HasAlpha);
}
