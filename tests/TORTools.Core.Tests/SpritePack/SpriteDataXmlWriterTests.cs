using System.Security.Cryptography;
using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// Byte-identity against SpriteForge's Go output for the SpriteTest fixture.
/// That transitively proves byte-identity against the Modding Kit, since
/// upstream SpriteForge already proves that in its own test suite.
/// </summary>
public class SpriteDataXmlWriterTests
{
    static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SpritePack", "Fixtures", name);

    [Fact]
    public void SpriteTest_fixture_matches_SpriteForge_output()
    {
        var expected = File.ReadAllBytes(FixturePath("SpriteTestSpriteData.xml"));

        // Reconstruct the inputs SpriteForge passes to its own buildXML for
        // this example. One category, one sheet, one sprite part.
        var cfg = new CategoryConfig { AlwaysLoad = true };
        var category = new SpriteCategory { Name = "ui_spritetest", Config = cfg, SpriteSheetCount = 1 };
        var sheet = new Sheet { Width = 128, Height = 128, Id = 1 };
        category.Sheets.Add(sheet);

        var part = new SpritePart
        {
            Name = "spritetest_icon",
            Category = "ui_spritetest",
            Path = "",
            Width = 64,
            Height = 64,
            SheetId = 1,
            SheetX = 4,
            SheetY = 4,
        };

        var sprite = new SpriteEntry { Name = "spritetest_icon", Part = part };

        var actual = SpriteDataXmlWriter.Build(
            new[] { category },
            new[] { part },
            new[] { sprite });

        actual.Length.Should().Be(expected.Length);
        Convert.ToHexString(SHA256.HashData(actual))
            .Should().Be(Convert.ToHexString(SHA256.HashData(expected)));
    }

    [Fact]
    public void Writer_emits_the_utf8_bom_and_declaration_without_trailing_newline()
    {
        var bytes = SpriteDataXmlWriter.Build(Array.Empty<SpriteCategory>(),
            Array.Empty<SpritePart>(), Array.Empty<SpriteEntry>());

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var tail = System.Text.Encoding.UTF8.GetString(bytes[^"</SpriteData>".Length..]);
        tail.Should().Be("</SpriteData>");
    }

    [Fact]
    public void Writer_escapes_markup_in_text_nodes()
    {
        var cfg = new CategoryConfig { AlwaysLoad = false };
        var category = new SpriteCategory { Name = "a&b<c>", Config = cfg, SpriteSheetCount = 0 };

        var bytes = SpriteDataXmlWriter.Build(new[] { category },
            Array.Empty<SpritePart>(), Array.Empty<SpriteEntry>());
        var xml = System.Text.Encoding.UTF8.GetString(bytes);
        xml.Should().Contain("<Name>a&amp;b&lt;c&gt;</Name>");
    }
}
