using System.Security.Cryptography;
using System.Text;
using TORTools.Core.SpritePack.Rdc;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// Hand-crafted RDC fixtures (no real Armory bytes involved) that exercise
/// the edge-case paths from <c>pack_rdc_exact</c>: empty files,
/// non-shrinking LZ4 fallback, multi-entry sort by (owner, data_id, type)
/// byte order, and the full 8-slot hash block.
/// </summary>
public class RdcSyntheticTests
{
    [Fact]
    public void Empty_rdc_round_trips_to_a_bare_header()
    {
        var file = new RdcFile();
        using var buffer = new MemoryStream();
        RdcWriter.Write(buffer, file);

        buffer.Length.Should().Be(RdcFile.HeaderSize);
        buffer.Position = 0;
        var reparsed = RdcReader.Read(buffer);
        reparsed.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Single_entry_lz4_round_trips_and_shrinks()
    {
        // Repeating ASCII compresses sharply — LZ4 will shrink it.
        var blob = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("sprite-pack-", 128)));
        var entry = new RdcEntry
        {
            OwnerGuid = Guid.NewGuid(),
            DataId = Guid.NewGuid(),
            TypeGuid = TpacGuids.TexPixels,
            Blob = blob,
            Compress = true,
        };
        entry.DataId = entry.OwnerGuid; // texture convention
        entry.Hashes[0] = 0xDEAD_BEEF_CAFE_BABE;
        entry.Hashes[1] = 0x0123_4567_89AB_CDEF;

        var file = new RdcFile();
        file.Entries.Add(entry);

        var round = RoundTrip(file);
        round.Entries.Should().ContainSingle();
        round.Entries[0].Blob.Should().Equal(blob);
        round.Entries[0].Hashes[0].Should().Be(0xDEAD_BEEF_CAFE_BABE);
        round.Entries[0].Hashes[1].Should().Be(0x0123_4567_89AB_CDEF);
        round.Entries[0].Compress.Should().BeTrue();
    }

    [Fact]
    public void Compression_falls_back_to_raw_when_lz4_does_not_shrink()
    {
        // Random bytes resist compression. Writer should fall back to raw
        // and clear the lz4 flag, matching pack_rdc_exact's "packed = comp
        // and len(st) < len(blob)" rule.
        var blob = new byte[48];
        new Random(2026).NextBytes(blob);

        var entry = new RdcEntry
        {
            OwnerGuid = Guid.NewGuid(),
            DataId = Guid.NewGuid(),
            TypeGuid = TpacGuids.TexPixels,
            Blob = blob,
            Compress = true,
        };

        var file = new RdcFile();
        file.Entries.Add(entry);

        var round = RoundTrip(file);
        round.Entries[0].Blob.Should().Equal(blob);
        // After round-trip we read the on-disk lz4 flag back — since the
        // writer cleared it, the parsed entry also reads Compress=false.
        round.Entries[0].Compress.Should().BeFalse();
    }

    [Fact]
    public void Writer_sorts_entries_by_owner_data_id_type_byte_order()
    {
        // Construct three entries in reverse sort order; the writer should
        // emit them in ascending (owner, data_id, type) order.
        var earliest = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var middle   = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var latest   = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var file = new RdcFile();
        file.Entries.Add(MakeTinyEntry(latest));
        file.Entries.Add(MakeTinyEntry(earliest));
        file.Entries.Add(MakeTinyEntry(middle));

        var round = RoundTrip(file);
        round.Entries.Select(e => e.OwnerGuid).Should().ContainInOrder(earliest, middle, latest);
    }

    [Fact]
    public void Hash_block_preserves_all_8_slots_round_trip()
    {
        var entry = new RdcEntry
        {
            OwnerGuid = Guid.NewGuid(),
            DataId = Guid.NewGuid(),
            TypeGuid = TpacGuids.TexPixels,
            Blob = new byte[] { 1, 2, 3, 4 },
            Compress = false,
        };
        for (var i = 0; i < RdcEntry.HashSlots; i++)
            entry.Hashes[i] = 0xAA00_0000_0000_0000ul | (ulong)i;

        var file = new RdcFile();
        file.Entries.Add(entry);

        var round = RoundTrip(file);
        for (var i = 0; i < RdcEntry.HashSlots; i++)
            round.Entries[0].Hashes[i].Should().Be(0xAA00_0000_0000_0000ul | (ulong)i);
    }

    [Fact]
    public void Writing_an_entry_with_more_than_8_hash_slots_throws()
    {
        var entry = new RdcEntry
        {
            OwnerGuid = Guid.NewGuid(),
            DataId = Guid.NewGuid(),
            TypeGuid = TpacGuids.TexPixels,
            Blob = new byte[] { 1 },
            Hashes = new ulong[RdcEntry.HashSlots + 1],
        };
        var file = new RdcFile();
        file.Entries.Add(entry);

        var act = () => { using var ms = new MemoryStream(); RdcWriter.Write(ms, file); };
        act.Should().Throw<ArgumentException>();
    }

    static RdcEntry MakeTinyEntry(Guid owner) => new()
    {
        OwnerGuid = owner,
        DataId = owner,
        TypeGuid = TpacGuids.TexPixels,
        Blob = new byte[] { (byte)owner.ToByteArray()[0] },
        Compress = false,
    };

    static RdcFile RoundTrip(RdcFile file)
    {
        using var buffer = new MemoryStream();
        RdcWriter.Write(buffer, file);
        buffer.Position = 0;
        return RdcReader.Read(buffer);
    }

    static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
