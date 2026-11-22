using System.Text;

namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Builders for the little variable-length blobs that live inside the two
/// auxiliary texture data segments (<see cref="TpacGuids.TexSettings"/> and
/// <see cref="TpacGuids.TexSrcInfo"/>). Hex-verified against
/// bannerlord-editor-free-sdk sdk/tpac.py:951 and the layout of
/// Modules/TOR_Armory/Assets/GauntletUI/ui_abilityicons_1_tex.tpac.
/// </summary>
public static class TextureDataBlobs
{
    public const int SettingsFixedPadding = 102;

    /// <summary>
    /// `SizedString(usage) + byte(do_not_compress) + byte(no_mips) + 102 zero bytes`.
    /// The two flag bytes are the Modding Kit's "Do Not Compress" and "No Mips"
    /// toggles. Observed in Armory UI atlases: usage="albedo",
    /// doNotCompress=false (DXT5 is compressed), noMips=true (single mip).
    /// </summary>
    public static byte[] BuildSettings(string usage, bool doNotCompress, bool noMips)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.WriteSizedString(usage);
            writer.Write(doNotCompress);
            writer.Write(noMips);
            writer.Write(new byte[SettingsFixedPadding]);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// `u32 w, u32 h, u32 depth=1, u32 array=1, u32 mips=1, SizedString(sourceFormat), u64 sourceSize`.
    /// For 2D sprite atlases depth/array/mips are all 1; sourceFormat is the
    /// PNG's native pixel format ("B8G8R8A8_UNORM" for RGBA, "B8G8R8" for RGB,
    /// "R8_UNORM" for grayscale); sourceSize is the raw PNG byte count.
    /// </summary>
    public static byte[] BuildSrcInfo(uint width, uint height, string sourceFormat, ulong sourcePngByteSize,
        uint depth = 1, uint array = 1, uint mips = 1)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(width);
            writer.Write(height);
            writer.Write(depth);
            writer.Write(array);
            writer.Write(mips);
            writer.WriteSizedString(sourceFormat);
            writer.Write(sourcePngByteSize);
        }
        return stream.ToArray();
    }
}
