using System.Text;

namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Serialises a <see cref="TpacFile"/> to Modding-Kit-shape bytes. Written
/// to match the hex layout of a real TPAC: header, asset-index region
/// (metadata blocks with sizes + segment tables), then the raw data region.
/// </summary>
public static class TpacWriter
{
    public static void Write(string path, TpacFile file)
    {
        using var stream = File.Create(path);
        Write(stream, file);
    }

    public static void Write(Stream stream, TpacFile file)
    {
        // The game stores each data segment at an absolute file offset in
        // the asset index. We have to know the full index-region size
        // before we can fill those offsets in, so we materialise the asset
        // records into byte buffers first, measure them, and then stream
        // everything out in one pass.
        var includeAssetVersion = file.Version > 1;
        var metadataBlobs = new List<byte[]>(file.Assets.Count);
        foreach (var asset in file.Assets)
        {
            metadataBlobs.Add(asset.Texture != null
                ? TextureMetadataCodec.Write(asset.Texture)
                : asset.RawMetadata);
        }

        var assetHeaderLengths = new List<int>(file.Assets.Count);
        for (var i = 0; i < file.Assets.Count; i++)
            assetHeaderLengths.Add(MeasureAssetRecord(file.Assets[i], metadataBlobs[i].Length, includeAssetVersion));

        var indexRegionSize = assetHeaderLengths.Sum();
        var dataRegionBase = (ulong)(TpacFile.HeaderSize + indexRegionSize);

        // Walk through the segments in asset order, assigning each one an
        // absolute offset into the data region.
        var cursor = dataRegionBase;
        foreach (var asset in file.Assets)
        foreach (var segment in asset.Segments)
        {
            segment.Offset = cursor;
            segment.StoredSize = (ulong)segment.StoredBytes.Length;
            cursor += segment.StoredSize;
        }

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(TpacFile.Magic);
        writer.Write(file.Version);
        writer.WriteGuid(file.PackageGuid);
        writer.Write((uint)file.Assets.Count);
        writer.Write((uint)indexRegionSize);
        writer.Write(file.Reserved);

        for (var i = 0; i < file.Assets.Count; i++)
            WriteAssetRecord(writer, file.Assets[i], metadataBlobs[i], includeAssetVersion);

        foreach (var asset in file.Assets)
        foreach (var segment in asset.Segments)
            writer.Write(segment.StoredBytes);
    }

    /// <summary>
    /// Bytes <see cref="WriteAssetRecord"/> will emit for this asset given a
    /// metadata blob of the stated length. Kept in lock-step with the writer.
    /// </summary>
    static int MeasureAssetRecord(TpacAsset asset, int metadataLength, bool includeAssetVersion)
    {
        const int guidSize = 16;
        const int segmentSize = 8 + 8 + 8 + guidSize + guidSize + 8 + 4 + 1;
        const int dependencySize = guidSize * 3;

        var size = guidSize                              // type guid
                   + guidSize                            // asset guid
                   + (includeAssetVersion ? 4 : 0)       // asset version
                   + 4                                   // name length prefix
                   + Encoding.UTF8.GetByteCount(asset.Name)
                   + 8                                   // metadata size prefix
                   + metadataLength                      // metadata bytes
                   + 8                                   // metadata checksum
                   + 4                                   // segment count
                   + asset.Segments.Count * segmentSize
                   + 4                                   // dependency count
                   + asset.Dependencies.Count * dependencySize;
        return size;
    }

    static void WriteAssetRecord(BinaryWriter writer, TpacAsset asset, byte[] metadata, bool includeAssetVersion)
    {
        writer.WriteGuid(asset.TypeGuid);
        writer.WriteGuid(asset.Guid);
        if (includeAssetVersion) writer.Write(asset.Version);
        writer.WriteSizedString(asset.Name);

        writer.Write((ulong)metadata.Length);
        writer.Write(metadata);
        writer.Write(asset.MetadataChecksum);

        writer.Write(asset.Segments.Count);
        foreach (var seg in asset.Segments)
        {
            writer.Write(seg.Offset);
            writer.Write(seg.ActualSize);
            writer.Write(seg.StoredSize);
            writer.WriteGuid(seg.OwnerGuid);
            writer.WriteGuid(seg.TypeGuid);
            writer.Write(seg.UnknownUlong);
            writer.Write(seg.UnknownUint);
            writer.Write((byte)seg.StorageFormat);
        }

        writer.Write(asset.Dependencies.Count);
        foreach (var dep in asset.Dependencies)
        {
            writer.WriteGuid(dep.Guid1);
            writer.WriteGuid(dep.Guid2);
            writer.WriteGuid(dep.Guid3);
        }
    }
}
