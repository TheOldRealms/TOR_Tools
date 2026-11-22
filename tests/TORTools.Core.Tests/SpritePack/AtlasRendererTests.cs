using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

public class AtlasRendererTests
{
    [Fact]
    public void Renders_a_single_sprite_onto_the_canvas_with_correct_padding()
    {
        var tempDir = Directory.CreateTempSubdirectory("spritepack_atlas_");
        try
        {
            // Make a 32×32 solid-red PNG that will serve as our sole part.
            var partPath = Path.Combine(tempDir.FullName, "red.png");
            using (var src = new Image<Rgba32>(32, 32, new Rgba32(255, 0, 0, 255)))
                src.SaveAsPng(partPath);

            var part = new SpritePart
            {
                Name = "red",
                Category = "ui_test",
                Path = partPath,
                Width = 32,
                Height = 32,
                SheetId = 1,
                SheetX = 4,
                SheetY = 8,
            };
            var sheet = new Sheet { Width = 64, Height = 64, EdgeSize = 4, Id = 1 };
            sheet.Parts.Add(part);

            var pngPath = Path.Combine(tempDir.FullName, "sheet.png");
            var buffer = AtlasRenderer.Render(sheet, pngPath);

            File.Exists(pngPath).Should().BeTrue();
            buffer.Length.Should().Be(64 * 64 * 4);

            // The placed region at (4,8) through (35,39) is red; the frame
            // around it is transparent.
            using var rendered = Image.Load<Rgba32>(pngPath);
            rendered.Width.Should().Be(64);
            rendered.Height.Should().Be(64);
            rendered[4, 8].Should().Be(new Rgba32(255, 0, 0, 255));
            rendered[35, 39].Should().Be(new Rgba32(255, 0, 0, 255));
            rendered[0, 0].Should().Be(new Rgba32(0, 0, 0, 0));
            rendered[36, 40].Should().Be(new Rgba32(0, 0, 0, 0));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Composes_two_parts_without_overlap_in_the_rgba_buffer()
    {
        var tempDir = Directory.CreateTempSubdirectory("spritepack_atlas_");
        try
        {
            var redPath   = Path.Combine(tempDir.FullName, "red.png");
            var bluePath  = Path.Combine(tempDir.FullName, "blue.png");
            using (var red  = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0, 255))) red.SaveAsPng(redPath);
            using (var blue = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 255, 255))) blue.SaveAsPng(bluePath);

            var sheet = new Sheet { Width = 64, Height = 32, EdgeSize = 0, Id = 1 };
            sheet.Parts.Add(new SpritePart
            {
                Name = "red", Category = "ui", Path = redPath,
                Width = 16, Height = 16, SheetX = 0, SheetY = 0,
            });
            sheet.Parts.Add(new SpritePart
            {
                Name = "blue", Category = "ui", Path = bluePath,
                Width = 16, Height = 16, SheetX = 20, SheetY = 0,
            });

            var pngPath = Path.Combine(tempDir.FullName, "sheet.png");
            var buffer = AtlasRenderer.Render(sheet, pngPath);

            // Red at (0,0); transparent in the gap at (18,0); blue at (20,0).
            PixelAt(buffer, 64, 0, 0).Should().Be((255, 0, 0, 255));
            PixelAt(buffer, 64, 18, 0).Should().Be((0, 0, 0, 0));
            PixelAt(buffer, 64, 20, 0).Should().Be((0, 0, 255, 255));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Throws_when_a_part_pngs_dimensions_do_not_match()
    {
        var tempDir = Directory.CreateTempSubdirectory("spritepack_atlas_");
        try
        {
            var path = Path.Combine(tempDir.FullName, "mismatch.png");
            using (var img = new Image<Rgba32>(10, 10)) img.SaveAsPng(path);

            var sheet = new Sheet { Width = 20, Height = 20, Id = 1 };
            sheet.Parts.Add(new SpritePart
            {
                Name = "wrong", Category = "ui", Path = path,
                Width = 20, Height = 20, SheetX = 0, SheetY = 0,
            });

            var act = () => AtlasRenderer.Render(sheet, Path.Combine(tempDir.FullName, "out.png"));
            act.Should().Throw<InvalidDataException>().WithMessage("*is 10x10, expected 20x20*");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    static (byte R, byte G, byte B, byte A) PixelAt(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }
}
