using System.Text;
using K4os.Compression.LZ4;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.SpritePack.Rdc;

/// <summary>
/// Serialises an <see cref="RdcFile"/> to disk using the layout and sort
/// rules the engine applies. Port of
/// <c>bannerlord-editor-free-sdk/sdk/tw_formats.py::pack_rdc_exact</c>.
/// </summary>
public static class RdcWriter
{
    public static void Write(string path, RdcFile file)
    {
        using var stream = File.Create(path);
        Write(stream, file);
    }

    public static void Write(Stream stream, RdcFile file)
    {
        // Compress each entry up-front so we know the stored size before
        // we start computing offsets. Each entry's StoredBytes buffer is
        // then concatenated in sort order after the 20-byte header + the
        // fixed-size record table.
        var records = new List<PackedRecord>(file.Entries.Count);
        foreach (var entry in file.Entries)
            records.Add(PackEntry(entry));

        // Sort by (owner, data_id, type) byte order. The sort has to
        // happen on the GUIDs because the on-disk layout is deterministic
        // regardless of the order the caller added entries.
        records.Sort(static (a, b) => CompareBytes(a.SortKey, b.SortKey));

        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(RdcFile.Magic);
        writer.Write(0u); // reserved
        writer.Write(records.Count);
        writer.Write((ulong)RdcFile.EntrySize * (ulong)records.Count);

        // Write the record table, with each payload assigned an absolute
        // offset into the data region (starts right after the table).
        var cursor = (ulong)(RdcFile.HeaderSize + RdcFile.EntrySize * records.Count);
        foreach (var rec in records)
        {
            writer.WriteGuid(rec.OwnerGuid);
            writer.WriteGuid(rec.DataId);
            writer.WriteGuid(rec.TypeGuid);
            writer.Write(cursor);
            writer.Write((ulong)rec.Blob.Length);
            writer.Write((ulong)rec.StoredBytes.Length);
            writer.Write(rec.TypeVersion);
            foreach (var h in rec.Hashes) writer.Write(h);
            writer.Write(0xFAFAFAFAu);
            writer.Write(rec.StoredLz4 ? (byte)1 : (byte)0);
            cursor += (ulong)rec.StoredBytes.Length;
        }

        foreach (var rec in records)
            writer.Write(rec.StoredBytes);
    }

    record struct PackedRecord(
        Guid OwnerGuid,
        Guid DataId,
        Guid TypeGuid,
        byte[] Blob,
        byte[] StoredBytes,
        bool StoredLz4,
        uint TypeVersion,
        ulong[] Hashes,
        byte[] SortKey);

    static PackedRecord PackEntry(RdcEntry entry)
    {
        var (storedBytes, storedLz4) = ApplyCompression(entry);

        // Hashes are always exactly 8 slots on disk. If the caller has
        // fewer, pad with zeros; if more, truncate with a loud exception
        // so we don't silently drop data.
        var hashes = new ulong[RdcEntry.HashSlots];
        if (entry.Hashes.Length > RdcEntry.HashSlots)
            throw new ArgumentException(
                $"RdcEntry has {entry.Hashes.Length} hash slots; max is {RdcEntry.HashSlots}.");
        for (var i = 0; i < entry.Hashes.Length; i++) hashes[i] = entry.Hashes[i];

        var typeVersion = entry.TypeVersionOverride ?? RdcTypeVersions.ForType(entry.TypeGuid);

        var sortKey = new byte[48];
        Buffer.BlockCopy(entry.OwnerGuid.ToByteArray(), 0, sortKey, 0, 16);
        Buffer.BlockCopy(entry.DataId.ToByteArray(), 0, sortKey, 16, 16);
        Buffer.BlockCopy(entry.TypeGuid.ToByteArray(), 0, sortKey, 32, 16);

        return new PackedRecord(
            entry.OwnerGuid, entry.DataId, entry.TypeGuid,
            entry.Blob, storedBytes, storedLz4,
            typeVersion, hashes, sortKey);
    }

    static (byte[] Stored, bool Lz4) ApplyCompression(RdcEntry entry)
    {
        if (!entry.Compress) return (entry.Blob, false);

        var maxLen = LZ4Codec.MaximumOutputSize(entry.Blob.Length);
        var buffer = new byte[maxLen];
        var written = LZ4Codec.Encode(entry.Blob, 0, entry.Blob.Length,
            buffer, 0, maxLen, LZ4Level.L12_MAX);

        // Engine rule: keep the LZ4 copy only when it actually shrinks the
        // blob. Otherwise fall back to the raw bytes and clear the lz4
        // flag so the loader doesn't try to decompress.
        if (written <= 0 || written >= entry.Blob.Length) return (entry.Blob, false);

        var compressed = new byte[written];
        Buffer.BlockCopy(buffer, 0, compressed, 0, written);
        return (compressed, true);
    }

    static int CompareBytes(byte[] a, byte[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var d = a[i] - b[i];
            if (d != 0) return d;
        }
        return a.Length - b.Length;
    }
}
