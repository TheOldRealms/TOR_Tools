using K4os.Compression.LZ4;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.SpritePack.Rdc;

/// <summary>
/// Parses a <c>.rdc</c> into an <see cref="RdcFile"/>. The on-disk
/// per-entry record layout follows the editor-free-sdk's
/// <c>pack_rdc_exact</c> format verbatim.
/// </summary>
public static class RdcReader
{
    public static RdcFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static RdcFile Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var file = new RdcFile();

        if (reader.ReadUInt32() != RdcFile.Magic)
            throw new InvalidDataException("Not an RDC file: wrong magic.");

        var reserved = reader.ReadUInt32();
        if (reserved != 0)
            throw new InvalidDataException($"RDC reserved field is {reserved}, expected 0.");

        var entryCount = reader.ReadInt32();
        var metadataSize = reader.ReadInt64();
        if (metadataSize != (long)RdcFile.EntrySize * entryCount)
            throw new InvalidDataException(
                $"RDC metadata size {metadataSize} != {RdcFile.EntrySize} * {entryCount}.");

        // Pass 1: read every entry's record and remember where its payload
        // lives.
        var payloadLocations = new List<PayloadLocation>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var (entry, location) = ReadEntryRecord(reader);
            file.Entries.Add(entry);
            payloadLocations.Add(location);
        }

        // Pass 2: pull each entry's payload bytes and decompress when
        // marked.
        for (var i = 0; i < file.Entries.Count; i++)
        {
            var entry = file.Entries[i];
            var loc = payloadLocations[i];
            stream.Position = (long)loc.Offset;
            var stored = reader.ReadBytes((int)loc.Stored);
            entry.Blob = loc.Lz4 ? Decompress(stored, loc.Raw) : stored;
            if (!loc.Lz4 && (ulong)stored.Length != loc.Raw)
                throw new InvalidDataException(
                    $"Raw-stored entry: {stored.Length} on disk, raw size claim {loc.Raw}.");
        }

        return file;
    }

    record struct PayloadLocation(ulong Offset, ulong Stored, ulong Raw, bool Lz4);

    static (RdcEntry, PayloadLocation) ReadEntryRecord(BinaryReader reader)
    {
        var entry = new RdcEntry
        {
            OwnerGuid = reader.ReadGuid(),
            DataId = reader.ReadGuid(),
            TypeGuid = reader.ReadGuid(),
        };
        var offset = reader.ReadUInt64();
        var raw = reader.ReadUInt64();
        var stored = reader.ReadUInt64();
        entry.TypeVersionOverride = reader.ReadUInt32();

        entry.Hashes = new ulong[RdcEntry.HashSlots];
        for (var i = 0; i < RdcEntry.HashSlots; i++)
            entry.Hashes[i] = reader.ReadUInt64();

        var magic = reader.ReadUInt32();
        if (magic != 0xFAFAFAFA)
            throw new InvalidDataException($"RDC entry magic {magic:X8} != 0xFAFAFAFA.");

        var lz4Flag = reader.ReadByte();
        entry.Compress = lz4Flag switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"RDC entry lz4 flag {lz4Flag} not 0 or 1."),
        };

        return (entry, new PayloadLocation(offset, stored, raw, entry.Compress));
    }

    static byte[] Decompress(byte[] stored, ulong raw)
    {
        var output = new byte[raw];
        var decoded = LZ4Codec.Decode(stored, 0, stored.Length, output, 0, output.Length);
        if (decoded != output.Length)
            throw new InvalidDataException(
                $"LZ4 decode produced {decoded} bytes, expected {raw}.");
        return output;
    }
}
