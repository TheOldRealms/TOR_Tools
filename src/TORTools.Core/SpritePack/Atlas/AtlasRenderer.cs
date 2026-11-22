using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Renders a <see cref="Sheet"/>'s placed parts onto a single transparent
/// RGBA8 canvas and writes it as a PNG. The raw RGBA8 buffer is returned
/// so downstream encoders (DXT5 or the inline RGBA8 payload) can reuse it
/// without re-decoding the PNG we just wrote. Port of SpriteForge's
/// <c>internal/sprite/render.go</c>.
/// </summary>
public static class AtlasRenderer
{
    /// <summary>
    /// Composes all parts belonging to <paramref name="sheet"/> onto an
    /// RGBA8 image of (<see cref="Sheet.Width"/>, <see cref="Sheet.Height"/>)
    /// and writes the atlas PNG to <paramref name="pngPath"/>. Any missing
    /// parent directory is created.
    /// </summary>
    /// <returns>
    /// Raw RGBA8 pixel bytes, row-major, no row padding — i.e.
    /// <c>Width * Height * 4</c> bytes in R,G,B,A order.
    /// </returns>
    public static byte[] Render(Sheet sheet, string pngPath)
    {
        using var canvas = new Image<Rgba32>(sheet.Width, sheet.Height, Color.Transparent);

        foreach (var part in sheet.Parts)
        {
            using var source = Image.Load<Rgba32>(part.Path);
            if (source.Width != part.Width || source.Height != part.Height)
                throw new InvalidDataException(
                    $"'{part.Path}': image is {source.Width}x{source.Height}, " +
                    $"expected {part.Width}x{part.Height}.");

            canvas.Mutate(ctx => ctx.DrawImage(source, new Point(part.SheetX, part.SheetY), 1f));
        }

        var dir = Path.GetDirectoryName(pngPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        canvas.SaveAsPng(pngPath);

        return ExtractRgba8(canvas);
    }

    /// <summary>
    /// Pulls the composed canvas's pixel bytes into a tightly-packed RGBA8
    /// buffer. Used by the BC encoder path so we don't have to go
    /// round-trip through the PNG we just wrote.
    /// </summary>
    static byte[] ExtractRgba8(Image<Rgba32> canvas)
    {
        var buffer = new byte[canvas.Width * canvas.Height * 4];
        canvas.CopyPixelDataTo(buffer);
        return buffer;
    }
}
