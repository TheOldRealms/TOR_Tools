using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

public class BinPackerTests
{
    [Fact]
    public void SpriteTest_input_packs_to_SpriteForge_layout()
    {
        // SpriteForge packs the one 64×64 icon into a sheet whose initial
        // bounds are 4096×4096 (the default), then RecalculateSize shrinks
        // it to 128×128 (next power of two ≥ 4 + 64 + 4 = 72). The placement
        // includes a 4-px default edge, so the sprite lands at (4, 4).
        var part = new SpritePart
        {
            Name = "spritetest_icon",
            Category = "ui_spritetest",
            Path = "irrelevant",
            Width = 64,
            Height = 64,
        };
        var category = new SpriteCategory
        {
            Name = "ui_spritetest",
            Config = new CategoryConfig { AlwaysLoad = true },
        };
        category.Parts.Add(part);

        var sheets = BinPacker.PackNormal(category, sheetW: 4096, sheetH: 4096, edge: 4);
        sheets.Should().ContainSingle();

        foreach (var sheet in sheets) BinPacker.RecalculateSize(sheet);

        var sheet0 = sheets[0];
        sheet0.Width.Should().Be(128);
        sheet0.Height.Should().Be(128);
        sheet0.Parts.Should().ContainSingle();

        part.SheetX.Should().Be(4);
        part.SheetY.Should().Be(4);
        part.SheetId.Should().Be(1);
    }

    [Fact]
    public void Packs_many_sprites_across_sheets_when_needed()
    {
        var category = new SpriteCategory
        {
            Name = "ui_test",
            Config = new CategoryConfig(),
        };
        // Three 150×150 sprites in a 200×200 sheet — each takes most of
        // the sheet, so no two fit together; expect three sheets.
        for (var i = 0; i < 3; i++)
        {
            category.Parts.Add(new SpritePart
            {
                Name = $"part_{i}",
                Category = "ui_test",
                Path = "",
                Width = 150,
                Height = 150,
            });
        }

        var sheets = BinPacker.PackNormal(category, sheetW: 200, sheetH: 200, edge: 0);
        sheets.Should().HaveCount(3);
        foreach (var sheet in sheets) sheet.Parts.Should().ContainSingle();
    }

    [Fact]
    public void Unique_packing_gives_one_sheet_per_part()
    {
        var category = new SpriteCategory
        {
            Name = "ui_unique",
            Config = new CategoryConfig { PackAllSpritesToUniqueTextures = true },
        };
        for (var i = 0; i < 3; i++)
        {
            category.Parts.Add(new SpritePart
            {
                Name = $"u{i}",
                Category = "ui_unique",
                Path = "",
                Width = 32 + i,
                Height = 32 + i,
            });
        }
        var sheets = BinPacker.PackUnique(category);
        sheets.Should().HaveCount(3);
        // Each sheet's dimensions equal its sprite's dimensions (unique = no padding).
        for (var i = 0; i < sheets.Count; i++)
        {
            sheets[i].Parts.Should().ContainSingle();
            sheets[i].Parts[0].SheetX.Should().Be(0);
            sheets[i].Parts[0].SheetY.Should().Be(0);
        }
    }

    [Fact]
    public void Oversize_parts_are_skipped_in_normal_packing()
    {
        var category = new SpriteCategory
        {
            Name = "ui_oversize",
            Config = new CategoryConfig(),
        };
        var normal = new SpritePart
        {
            Name = "normal",
            Category = "ui_oversize",
            Path = "",
            Width = 50,
            Height = 50,
        };
        var oversize = new SpritePart
        {
            Name = "huge",
            Category = "ui_oversize",
            Path = "",
            Width = 300,
            Height = 300,
        };
        category.Parts.Add(normal);
        category.Parts.Add(oversize);

        var sheets = BinPacker.PackNormal(category, sheetW: 200, sheetH: 200, edge: 0);
        sheets.Should().HaveCount(1);
        sheets[0].Parts.Should().ContainSingle().Which.Should().BeSameAs(normal);
    }
}
