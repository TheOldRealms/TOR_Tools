using System.Security.Cryptography;
using System.Text;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Deterministic UUIDv5 helpers for SpritePack assets. Byte-identical with
/// SpriteForge's <c>internal/tpac/guid.go</c> so output stays reproducible
/// across the Go fork and this C# port.
/// </summary>
public static class SpriteGuids
{
    /// <summary>
    /// Namespace used for texture-asset GUIDs: <c>UUIDv5(AssetNamespace,
    /// module + "/" + name)</c>. The value is a frozen SpriteForge choice
    /// (<c>internal/tpac/format.go</c>); don't change it or every re-pack
    /// would re-churn every GUID.
    /// </summary>
    public static readonly Guid AssetNamespace =
        new("a0f6493d-1a44-4e33-bcb4-2ebd2ebcf713");

    /// <summary>
    /// UUIDv5 = SHA-1 of (<paramref name="namespaceGuid"/> bytes in
    /// canonical RFC 4122 big-endian order concatenated with
    /// <paramref name="name"/> UTF-8 bytes), with version 5 + RFC 4122
    /// variant bits applied to the first 16 bytes.
    /// </summary>
    public static Guid UuidV5(Guid namespaceGuid, string name)
    {
        var nsBytes = GuidToRfc4122Bytes(namespaceGuid);
        var nameBytes = Encoding.UTF8.GetBytes(name);

        var buffer = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, buffer, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, nsBytes.Length, nameBytes.Length);

        var sha1 = SHA1.HashData(buffer);
        var guidBytes = new byte[16];
        Buffer.BlockCopy(sha1, 0, guidBytes, 0, 16);

        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return GuidFromRfc4122Bytes(guidBytes);
    }

    /// <summary>
    /// Deterministic texture-asset GUID: <c>UUIDv5(AssetNamespace, module + "/" + name)</c>.
    /// </summary>
    public static Guid AssetGuid(string module, string name) =>
        UuidV5(AssetNamespace, module + "/" + name);

    /// <summary>
    /// SpriteForge uses one fixed package-level GUID for every TPAC it
    /// emits (<c>internal/tpac/format.go::PackageGUID</c>). That matches
    /// what the Modding Kit does for sprite packages. Change it and every
    /// previously-built atlas would appear to be a different package to
    /// the game.
    /// </summary>
    public static readonly Guid FixedPackageGuid =
        new("807e3d8d-e501-499b-a1d3-22ae3f4e64f3");

    // --- RFC 4122 <-> .NET Guid byte-order conversion ---------------------

    /// <summary>
    /// Returns the 16 GUID bytes in RFC 4122 "network" order (first three
    /// chunks big-endian) — matches the textual GUID layout.
    /// SpriteForge's Go code calls this the "canonical" order.
    /// </summary>
    static byte[] GuidToRfc4122Bytes(Guid guid)
    {
        var le = guid.ToByteArray();
        return new[]
        {
            le[3], le[2], le[1], le[0], // Data1 LE -> BE
            le[5], le[4],               // Data2 LE -> BE
            le[7], le[6],               // Data3 LE -> BE
            le[8], le[9], le[10], le[11], le[12], le[13], le[14], le[15], // Data4 unchanged
        };
    }

    static Guid GuidFromRfc4122Bytes(byte[] be)
    {
        var le = new[]
        {
            be[3], be[2], be[1], be[0],
            be[5], be[4],
            be[7], be[6],
            be[8], be[9], be[10], be[11], be[12], be[13], be[14], be[15],
        };
        return new Guid(le);
    }
}
