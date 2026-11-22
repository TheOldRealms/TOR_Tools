namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Parses a Bannerlord TPAC file into a <see cref="TpacFile"/>. Loads the
/// full data region into memory so the writer can round-trip payload bytes
/// without re-compressing. For gauntlet-UI atlases that's a handful of MB
/// at most — the memory cost is not worth avoiding.
/// </summary>
public static class TpacReader
{
    public static TpacFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static TpacFile Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var file = new TpacFile();

        if (reader.ReadUInt32() != TpacFile.Magic)
            throw new InvalidDataException("Not a TPAC file: wrong magic.");

        file.Version = reader.ReadInt32();
        if (file.Version is not 1 and not 2)
            throw new NotSupportedException($"Unsupported TPAC version: {file.Version}");

        file.PackageGuid = reader.ReadGuid();
        var resourceCount = reader.ReadUInt32();
        var dataOffset = reader.ReadUInt32();
        file.Reserved = reader.ReadUInt32();

        var includeAssetVersion = file.Version > 1;

        // Pass 1: read every asset's header + metadata + segment table.
        for (var i = 0; i < resourceCount; i++)
            file.Assets.Add(ReadAssetHeader(reader, includeAssetVersion));

        // Pass 2: now that we know every segment's absolute offset + stored
        // size, seek back and slurp the raw payload bytes.
        foreach (var asset in file.Assets)
        foreach (var segment in asset.Segments)
        {
            stream.Position = (long)segment.Offset;
            segment.StoredBytes = reader.ReadBytes((int)segment.StoredSize);
        }

        return file;
    }

    static TpacAsset ReadAssetHeader(BinaryReader reader, bool includeAssetVersion)
    {
        var asset = new TpacAsset
        {
            TypeGuid = reader.ReadGuid(),
            Guid = reader.ReadGuid(),
        };
        if (includeAssetVersion) asset.Version = reader.ReadUInt32();
        asset.Name = reader.ReadSizedString();

        var metadataSize = (int)reader.ReadUInt64();
        var metadataStart = reader.BaseStream.Position;
        var metadataBytes = reader.ReadBytes(metadataSize);

        if (asset.TypeGuid == TpacGuids.Texture)
            asset.Texture = TextureMetadataCodec.Read(metadataBytes);
        else
            asset.RawMetadata = metadataBytes;

        // Guard against tail discipline bugs in nested readers — we must
        // always land exactly at metadataStart + metadataSize before reading
        // the checksum.
        reader.BaseStream.Position = metadataStart + metadataSize;

        asset.MetadataChecksum = reader.ReadInt64();

        var segmentCount = reader.ReadInt32();
        for (var i = 0; i < segmentCount; i++)
        {
            asset.Segments.Add(new DataSegment
            {
                Offset = reader.ReadUInt64(),
                ActualSize = reader.ReadUInt64(),
                StoredSize = reader.ReadUInt64(),
                OwnerGuid = reader.ReadGuid(),
                TypeGuid = reader.ReadGuid(),
                UnknownUlong = reader.ReadUInt64(),
                UnknownUint = reader.ReadUInt32(),
                StorageFormat = (SegmentStorageFormat)reader.ReadByte(),
            });
        }

        var depCount = reader.ReadInt32();
        for (var i = 0; i < depCount; i++)
        {
            asset.Dependencies.Add(new Dependency
            {
                Guid1 = reader.ReadGuid(),
                Guid2 = reader.ReadGuid(),
                Guid3 = reader.ReadGuid(),
            });
        }

        return asset;
    }
}

/// <summary>
/// Reads and writes <see cref="TextureMetadata"/> blocks. The block-length
/// prefix is handled by the caller; this codec only knows field order.
/// </summary>
internal static class TextureMetadataCodec
{
    public static TextureMetadata Read(byte[] metadata)
    {
        using var stream = new MemoryStream(metadata);
        using var reader = new BinaryReader(stream);
        var tex = new TextureMetadata
        {
            MetadataVersion = reader.ReadUInt32(),
            BillboardMaterialGuid = reader.ReadGuid(),
            UnknownUint1 = reader.ReadUInt32(),
            Source = reader.ReadSizedString(),
            SourceHash = reader.ReadUInt64(),
            UnknownBool = reader.ReadBoolean(),
            UnknownUint2 = reader.ReadUInt32(),
            Flags = reader.ReadStringList(),
            UnknownUint3 = reader.ReadUInt32(),
            UnknownByte = reader.ReadByte(),
            Width = reader.ReadUInt32(),
            Height = reader.ReadUInt32(),
            UnknownUint4 = reader.ReadUInt32(),
            MipmapCount = reader.ReadByte(),
            ArrayCount = reader.ReadUInt16(),
            Format = reader.ReadSizedString(),
            UnknownUint5 = reader.ReadUInt32(),
            SystemFlags = reader.ReadStringList(),
        };

        if (tex.UnknownUint3 > 0)
        {
            tex.UnknownUint6 = reader.ReadUInt32();
            tex.UnknownUint7 = reader.ReadUInt32();
        }

        // GeneratedAssets: TaleWorlds introduced a tuple list in 1.5.0
        // without bumping the metadata version. The TpacTool heuristic
        // ("if MetadataVersion >= 1 or exactly 4 bytes remain") works; we
        // simplify by always reading on MetadataVersion >= 1 which covers
        // all current fixtures.
        if (tex.MetadataVersion >= 1)
        {
            var pairCount = reader.ReadUInt32();
            for (var i = 0; i < pairCount; i++)
                tex.GeneratedAssets.Add((reader.ReadGuid(), reader.ReadGuid()));
        }

        if (tex.MetadataVersion >= 2)
            tex.UnknownUlong2 = reader.ReadUInt64();

        // Preserve any trailing bytes we don't know how to parse yet so the
        // writer can emit them verbatim.
        var remaining = metadata.Length - stream.Position;
        if (remaining > 0) tex.TrailingMetadata = reader.ReadBytes((int)remaining);

        return tex;
    }

    public static byte[] Write(TextureMetadata tex)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(tex.MetadataVersion);
            writer.WriteGuid(tex.BillboardMaterialGuid);
            writer.Write(tex.UnknownUint1);
            writer.WriteSizedString(tex.Source);
            writer.Write(tex.SourceHash);
            writer.Write(tex.UnknownBool);
            writer.Write(tex.UnknownUint2);
            writer.WriteStringList(tex.Flags);
            writer.Write(tex.UnknownUint3);
            writer.Write(tex.UnknownByte);
            writer.Write(tex.Width);
            writer.Write(tex.Height);
            writer.Write(tex.UnknownUint4);
            writer.Write(tex.MipmapCount);
            writer.Write(tex.ArrayCount);
            writer.WriteSizedString(tex.Format);
            writer.Write(tex.UnknownUint5);
            writer.WriteStringList(tex.SystemFlags);

            if (tex.UnknownUint3 > 0)
            {
                writer.Write(tex.UnknownUint6);
                writer.Write(tex.UnknownUint7);
            }

            if (tex.MetadataVersion >= 1)
            {
                writer.Write(tex.GeneratedAssets.Count);
                foreach (var (typeGuid, assetGuid) in tex.GeneratedAssets)
                {
                    writer.WriteGuid(typeGuid);
                    writer.WriteGuid(assetGuid);
                }
            }

            if (tex.MetadataVersion >= 2)
                writer.Write(tex.UnknownUlong2);

            if (tex.TrailingMetadata.Length > 0) writer.Write(tex.TrailingMetadata);
        }
        return stream.ToArray();
    }
}
