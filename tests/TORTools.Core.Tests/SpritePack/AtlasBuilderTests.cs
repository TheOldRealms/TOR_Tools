using TORTools.Core.SpritePack.Atlas;
using TORTools.Core.SpritePack.Rdc;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// End-to-end test of the SpritePack pipeline on SpriteForge's bundled
/// SpriteTest example: config + atlas PNGs + SpriteData.xml + TPAC + RDC
/// all land where the game expects them, with the Modding-Kit segment
/// shape (TEX_SETTINGS + TEX_SRCINFO in the TPAC, pixels in the RDC).
/// </summary>
public class AtlasBuilderTests
{
    static readonly string SpriteTestFixture =
        Path.Combine(AppContext.BaseDirectory, "SpritePack", "Fixtures", "SpriteTestModule");

    [Fact]
    public void Builds_SpriteTest_end_to_end()
    {
        // The SpriteTest fixture is a trimmed copy of SpriteForge's own
        // examples/SpriteTest/ bundled under test Fixtures. We copy it
        // again into a temp folder so the build can emit outputs next to
        // the inputs without mutating the committed fixture.
        if (!Directory.Exists(SpriteTestFixture))
            throw new InvalidOperationException(
                $"SpriteTest fixture missing at '{SpriteTestFixture}'. " +
                "Check tests/TORTools.Core.Tests/SpritePack/Fixtures/SpriteTestModule/.");

        var tempDir = Directory.CreateTempSubdirectory("spritepack_e2e_");
        try
        {
            var moduleDir = Path.Combine(tempDir.FullName, "SpriteTest");
            CopyTree(SpriteTestFixture, moduleDir);

            var report = AtlasBuilder.Build(moduleDir);

            report.Module.Should().Be("SpriteTest");
            report.Categories.Should().ContainSingle(c => c == "ui_spritetest");
            report.Sheets.Should().ContainSingle();
            report.Sheets[0].Width.Should().Be(128);
            report.Sheets[0].Height.Should().Be(128);
            report.Sheets[0].Format.Should().Be("R8G8B8A8_UNORM"); // no <Compress/>
            report.TpacPaths.Should().ContainSingle();
            report.RdcPaths.Should().ContainSingle();

            // 1. Atlas PNG exists and is 128×128 (the packer's power-of-two
            // shrink from 4096 with one 64×64 sprite at (4,4)).
            var pngPath = Path.Combine(moduleDir, "AssetSources", "GauntletUI", "ui_spritetest_1.png");
            File.Exists(pngPath).Should().BeTrue();

            // 2. SpriteData.xml written next to the module.
            var xmlPath = Path.Combine(moduleDir, "GUI", "SpriteTestSpriteData.xml");
            File.Exists(xmlPath).Should().BeTrue();

            // 3. TPAC: 1 texture asset with 2 data segments (SETTINGS + SRCINFO).
            var tpacPath = Path.Combine(moduleDir, "AssetPackages", "SpriteTest_ui.tpac");
            File.Exists(tpacPath).Should().BeTrue();
            var tpac = TpacReader.Read(tpacPath);
            tpac.PackageGuid.Should().Be(SpriteGuids.FixedPackageGuid);
            tpac.Assets.Should().ContainSingle();
            var asset = tpac.Assets[0];
            asset.Name.Should().Be("ui_spritetest_1");
            asset.TypeGuid.Should().Be(TpacGuids.Texture);
            asset.Guid.Should().Be(SpriteGuids.AssetGuid("SpriteTest", "ui_spritetest_1"));
            asset.Segments.Should().HaveCount(2);
            asset.Segments[0].TypeGuid.Should().Be(TpacGuids.TexSettings);
            asset.Segments[1].TypeGuid.Should().Be(TpacGuids.TexSrcInfo);

            // 4. RDC: 1 entry with TEX_PIXELS.
            var rdc = RdcReader.Read(report.RdcPaths[0]);
            rdc.Entries.Should().ContainSingle();
            rdc.Entries[0].TypeGuid.Should().Be(TpacGuids.TexPixels);
            rdc.Entries[0].OwnerGuid.Should().Be(asset.Guid);
            rdc.Entries[0].Compress.Should().BeTrue();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Per_category_output_writes_one_tpac_per_category()
    {
        var tempDir = Directory.CreateTempSubdirectory("spritepack_percat_");
        try
        {
            // Build a two-category module on the fly.
            var moduleDir = Path.Combine(tempDir.FullName, "Testbed");
            Directory.CreateDirectory(Path.Combine(moduleDir, "GUI", "SpriteParts", "ui_cat_a"));
            Directory.CreateDirectory(Path.Combine(moduleDir, "GUI", "SpriteParts", "ui_cat_b"));
            File.WriteAllText(Path.Combine(moduleDir, "GUI", "SpriteParts", "Config.xml"),
                "<Config><SpriteCategory Name=\"ui_cat_a\"/><SpriteCategory Name=\"ui_cat_b\"/></Config>");
            WriteSolidPng(Path.Combine(moduleDir, "GUI", "SpriteParts", "ui_cat_a", "alpha.png"), 32, 32, 255, 0, 0, 255);
            WriteSolidPng(Path.Combine(moduleDir, "GUI", "SpriteParts", "ui_cat_b", "beta.png"),  32, 32, 0, 0, 255, 255);

            var report = AtlasBuilder.Build(moduleDir, new AtlasOptions { PerCategory = true });

            report.Categories.Should().Equal("ui_cat_a", "ui_cat_b");
            report.TpacPaths.Should().HaveCount(2);
            Path.GetFileName(report.TpacPaths[0]).Should().Be("Testbed_ui_cat_a.tpac");
            Path.GetFileName(report.TpacPaths[1]).Should().Be("Testbed_ui_cat_b.tpac");
            report.RdcPaths.Should().ContainSingle(); // one RDC file, both pixel entries inside
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }

    static void WriteSolidPng(string path, int w, int h, byte r, byte g, byte b, byte a)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            w, h, new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, a));
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, path);
    }
}
