using System.ComponentModel;
using ModelContextProtocol.Server;
using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools that drive the SpritePack atlas pipeline — turn a module's
/// <c>GUI/SpriteParts/</c> folder into game-loadable sprite atlas files
/// (atlas PNGs + <c>SpriteData.xml</c> + <c>.tpac</c> + <c>.rdc</c>).
/// </summary>
[McpServerToolType]
public class SpritePackTools
{
    [McpServerTool, Description(
        "Build a Bannerlord sprite atlas from a module folder. Reads " +
        "<ModuleDir>/GUI/SpriteParts/<category>/*.png plus Config.xml and " +
        "writes atlas PNGs under AssetSources/GauntletUI/, " +
        "<Module>SpriteData.xml under GUI/, the texture package under " +
        "AssetPackages/, and the runtime data cache under " +
        "RuntimeDataCache/. Returns the paths it wrote plus any warnings. " +
        "The caller is responsible for the git review + commit; this tool " +
        "mutates the module folder directly, same as the Modding Kit's " +
        "Import Resources.")]
    public PackSpritesResult pack_sprites(
        [Description("Absolute path to the module folder (e.g. /path/to/Modules/TOR_Armory).")]
        string module_dir,
        [Description("When true, writes one .tpac per sprite category (<Mod>_<cat>.tpac) instead of one combined <Mod>_ui.tpac. Recommended for modules with many categories to keep git diffs tight.")]
        bool per_category = false,
        [Description("Sheet size in pixels; Config.xml cannot override this. Default 4096.")]
        int sheet_size = 4096,
        [Description("Default padding (pixels) around each placed sprite. Config.xml EdgeSize per category overrides it. Default 4.")]
        int edge_size = 4,
        [Description("Pixel format: 'auto' (uses Config.xml <Compress/> per category), 'rgba8' (force uncompressed), or 'dxt5' (force block-compressed). Default 'auto'.")]
        string format = "auto")
    {
        try
        {
            var options = new AtlasOptions
            {
                PerCategory = per_category,
                SheetSize = sheet_size,
                EdgeSize = edge_size,
                Format = ParseFormat(format),
            };
            var report = AtlasBuilder.Build(module_dir, options);

            return new PackSpritesResult
            {
                Ok = true,
                Module = report.Module,
                ModuleDir = report.ModuleDir,
                Categories = report.Categories,
                Sheets = report.Sheets.Select(s => new PackedSheet
                {
                    Name = s.Name,
                    Category = s.Category,
                    Width = s.Width,
                    Height = s.Height,
                    Format = s.Format,
                }).ToList(),
                SpriteDataXml = report.XmlPath,
                SheetDir = report.SheetDir,
                TpacPaths = report.TpacPaths,
                RdcPaths = report.RdcPaths,
                Warnings = report.Warnings,
            };
        }
        catch (Exception ex)
        {
            return new PackSpritesResult
            {
                Ok = false,
                Error = $"{ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    static FormatMode ParseFormat(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "auto" => FormatMode.Auto,
        "rgba8" => FormatMode.Rgba8,
        "dxt5" or "bc3" => FormatMode.Dxt5,
        _ => throw new ArgumentException($"unknown format '{value}' (want auto, rgba8, dxt5)"),
    };
}

public sealed class PackSpritesResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? Module { get; init; }
    public string? ModuleDir { get; init; }
    public List<string> Categories { get; init; } = new();
    public List<PackedSheet> Sheets { get; init; } = new();
    public string? SpriteDataXml { get; init; }
    public string? SheetDir { get; init; }
    public List<string> TpacPaths { get; init; } = new();
    public List<string> RdcPaths { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class PackedSheet
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string Format { get; init; }
}
