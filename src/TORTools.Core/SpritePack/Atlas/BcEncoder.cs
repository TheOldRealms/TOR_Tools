using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Encodes an RGBA8 atlas into the two pixel formats sprite-pack supports:
/// uncompressed RGBA8 (bytes-through) and DXT5 (BC3) block-compressed.
/// Thin wrapper over <c>BCnEncoder.Net</c> — pure .NET, zero native deps.
/// </summary>
/// <remarks>
/// The matching
/// <see cref="TORTools.Core.SpritePack.Tpac.TextureMetadata.Format"/>
/// strings for these are <c>"R8G8B8A8_UNORM"</c> and <c>"DXT5"</c>.
/// </remarks>
public static class BcEncoder
{
    /// <summary>RGBA8 passthrough. Same bytes in, same bytes out.</summary>
    public static byte[] EncodeRgba8(byte[] rgba8) => rgba8;

    /// <summary>
    /// Compresses an RGBA8 buffer into DXT5 (BC3) blocks. Dimensions must
    /// be multiples of 4 — DXT5 encodes 4×4 pixel blocks and the Modding
    /// Kit refuses textures whose edge blocks would be padded.
    /// </summary>
    public static byte[] EncodeDxt5(byte[] rgba8, int width, int height)
    {
        if (width % 4 != 0 || height % 4 != 0)
            throw new ArgumentException(
                $"DXT5 requires dimensions divisible by 4; got {width}x{height}.");
        if (rgba8.Length != width * height * 4)
            throw new ArgumentException(
                $"RGBA8 buffer is {rgba8.Length} bytes for {width}x{height}; expected {width * height * 4}.");

        var encoder = new BCnEncoder.Encoder.BcEncoder
        {
            OutputOptions =
            {
                Format = CompressionFormat.Bc3, // = DXT5
                GenerateMipMaps = false,
                Quality = CompressionQuality.Balanced,
            },
        };

        var pixels = new ColorRgba32[height, width];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var o = (y * width + x) * 4;
            pixels[y, x] = new ColorRgba32(rgba8[o], rgba8[o + 1], rgba8[o + 2], rgba8[o + 3]);
        }
        // EncodeToRawBytes returns one byte[] per mip; we asked for no
        // mips, so there's a single buffer at [0].
        return encoder.EncodeToRawBytes(pixels)[0];
    }
}
