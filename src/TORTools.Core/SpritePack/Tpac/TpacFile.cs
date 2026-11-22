namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// A parsed Bannerlord TPAC package. Equivalent of TpacTool.Lib.AssetPackage,
/// but stripped down to what Modding-Kit sprite atlases actually need: the
/// package GUID, one or more texture assets, and whatever other asset types
/// show up as pass-through bytes (preserved verbatim on round-trip).
/// </summary>
public sealed class TpacFile
{
    public const uint Magic = 0x43415054; // "TPAC"
    public const int DefaultVersion = 2;
    public const int HeaderSize = 36;

    public int Version { get; set; } = DefaultVersion;
    public Guid PackageGuid { get; set; }
    public uint Reserved { get; set; }

    public List<TpacAsset> Assets { get; } = new();
}

/// <summary>
/// One asset entry inside a TPAC. Textures use a structured
/// <see cref="TextureMetadata"/>; everything else keeps its metadata as raw
/// bytes so round-trips stay exact without the writer having to understand
/// every asset type.
/// </summary>
public sealed class TpacAsset
{
    public Guid TypeGuid { get; set; }
    public Guid Guid { get; set; }
    public uint Version { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Structured metadata when <see cref="TypeGuid"/> is
    /// <see cref="TpacGuids.Texture"/>; null otherwise.
    /// </summary>
    public TextureMetadata? Texture { get; set; }

    /// <summary>
    /// Raw metadata bytes for assets we don't decode (non-texture types) or
    /// for the trailing fields of a texture whose layout we don't know in
    /// full. For textures, <see cref="TextureMetadata.TrailingMetadata"/>
    /// carries those bytes instead and this field is unused.
    /// </summary>
    public byte[] RawMetadata { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Checksum field the loader records between the metadata block and the
    /// segment list. Preserved verbatim on round-trip (the game's loader
    /// stores but doesn't appear to recompute it).
    /// </summary>
    public long MetadataChecksum { get; set; }

    public List<DataSegment> Segments { get; } = new();
    public List<Dependency> Dependencies { get; } = new();
}

/// <summary>
/// Pointer to a data chunk at the end of the TPAC. The payload lives at
/// <see cref="Offset"/> and spans <see cref="StoredSize"/> bytes (LZ4-compressed
/// when <see cref="StorageFormat"/> is <see cref="SegmentStorageFormat.Lz4"/>,
/// or uncompressed when it is <see cref="SegmentStorageFormat.Raw"/>).
/// </summary>
public sealed class DataSegment
{
    public ulong Offset { get; set; }
    public ulong ActualSize { get; set; }
    public ulong StoredSize { get; set; }
    public Guid OwnerGuid { get; set; }
    public Guid TypeGuid { get; set; }
    public ulong UnknownUlong { get; set; }
    public uint UnknownUint { get; set; }
    public SegmentStorageFormat StorageFormat { get; set; }

    /// <summary>
    /// Raw stored bytes at <see cref="Offset"/>. Populated on read so the
    /// writer can emit the payload again verbatim (keeps round-trips exact
    /// without re-compressing).
    /// </summary>
    public byte[] StoredBytes { get; set; } = Array.Empty<byte>();
}

public enum SegmentStorageFormat : byte
{
    Raw = 0,
    Lz4 = 1,
}

public sealed class Dependency
{
    public Guid Guid1 { get; set; }
    public Guid Guid2 { get; set; }
    public Guid Guid3 { get; set; }
}
