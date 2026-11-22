using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.SpritePack.Rdc;

/// <summary>
/// One entry in a RuntimeDataCache file. The engine looks entries up by the
/// tuple <c>(OwnerGuid, DataId, TypeGuid)</c>; everything else is payload
/// metadata or the blob itself. Field layout mirrors the 145-byte on-disk
/// record documented in the editor-free-sdk at
/// <c>bannerlord-editor-free-sdk/sdk/tpac.py:60-90</c>.
/// </summary>
public sealed class RdcEntry
{
    public Guid OwnerGuid { get; set; }

    /// <summary>
    /// For textures, equals <see cref="OwnerGuid"/>. For meshes, the
    /// submesh resource. For clip caches, the clip resource (also equals
    /// owner).
    /// </summary>
    public Guid DataId { get; set; }

    public Guid TypeGuid { get; set; }

    /// <summary>
    /// The raw blob before compression. On read, populated after any
    /// decompression. On write, the source bytes the writer compresses (or
    /// not) via the <see cref="Compress"/> flag.
    /// </summary>
    public byte[] Blob { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The 8 u64 "hash" slots stored in the 64-byte hash block. Semantics
    /// depend on <see cref="TypeGuid"/>; see <see cref="HashSlots"/>.
    /// Entries read from disk preserve the full 8 slots verbatim so the
    /// writer can produce byte-identical output.
    /// </summary>
    public ulong[] Hashes { get; set; } = new ulong[HashSlots];

    /// <summary>
    /// If true, the writer tries LZ4 and keeps the compressed copy only
    /// when it shrinks the blob (the engine's own rule, see the
    /// editor-free-sdk <c>pack_rdc_exact</c> implementation).
    /// </summary>
    public bool Compress { get; set; }

    /// <summary>
    /// The engine stores a type-dependent <c>u32</c> after the sizes in
    /// every entry record (textures 0, mesh vertex stream 1, clips 2). The
    /// reader captures whatever was on disk so round-trip is byte-exact
    /// even when we encounter a type GUID we haven't catalogued yet; the
    /// writer falls back to <see cref="RdcTypeVersions.ForType"/> when null.
    /// </summary>
    public uint? TypeVersionOverride { get; set; }

    /// <summary>64-byte hash block divided by sizeof(u64).</summary>
    public const int HashSlots = 8;
}
