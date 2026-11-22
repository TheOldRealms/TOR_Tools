using System.Security.Cryptography;
using TORTools.Core.SpritePack.Rdc;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// Round-trip real TOR_Armory RDC files through <see cref="RdcReader"/> and
/// <see cref="RdcWriter"/>; byte-identity proves we understand the
/// pack_rdc_exact format at the hex level.
/// </summary>
public class RdcRoundTripTests
{
    static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SpritePack", "Fixtures", name);

    [Fact]
    public void Tiny_clip_cache_rdc_round_trips_byte_identical()
    {
        var path = FixturePath("tiny.rdc");

        var original = File.ReadAllBytes(path);
        var parsed = RdcReader.Read(path);

        using var buffer = new MemoryStream();
        RdcWriter.Write(buffer, parsed);
        var written = buffer.ToArray();

        written.Length.Should().Be(original.Length);
        Sha256(written).Should().Be(Sha256(original));
    }

    [Fact]
    public void Abilityicons_pixel_rdc_round_trips_byte_identical()
    {
        var path = FixturePath("abilityicons.rdc");

        var original = File.ReadAllBytes(path);
        var parsed = RdcReader.Read(path);

        using var buffer = new MemoryStream();
        RdcWriter.Write(buffer, parsed);
        var written = buffer.ToArray();

        written.Length.Should().Be(original.Length);
        Sha256(written).Should().Be(Sha256(original));
    }

    [Fact]
    public void Tiny_clip_cache_rdc_exposes_the_expected_entry_shape()
    {
        var file = RdcReader.Read(FixturePath("tiny.rdc"));
        file.Entries.Should().HaveCount(1);

        var entry = file.Entries.Single();
        entry.TypeGuid.Should().Be(RdcTypeVersions.ClipCache);
        entry.OwnerGuid.Should().Be(entry.DataId); // clip cache convention: owner == data id
        entry.Compress.Should().BeTrue();
        entry.TypeVersionOverride.Should().Be(2);
        entry.Hashes[0].Should().NotBe(0ul); // h1 = xxh64(source animation)
        entry.Hashes[1].Should().NotBe(0ul); // h2 = xxh64(clip record hash slot)
        for (var i = 2; i < RdcEntry.HashSlots; i++)
            entry.Hashes[i].Should().Be(0ul, "slots 2-7 are always zero in Armory's fixtures");
    }

    [Fact]
    public void Abilityicons_rdc_matches_the_tpac_asset_guid()
    {
        var rdc = RdcReader.Read(FixturePath("abilityicons.rdc"));
        var tpac = TpacReader.Read(FixturePath("ui_abilityicons_1_tex.tpac"));

        var rdcEntry = rdc.Entries.Single();
        var tpacAsset = tpac.Assets.Single();
        rdcEntry.OwnerGuid.Should().Be(tpacAsset.Guid, "the RDC references the matching TPAC asset by guid");
        rdcEntry.DataId.Should().Be(tpacAsset.Guid);
        rdcEntry.TypeGuid.Should().Be(TpacGuids.TexPixels);
    }

    static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
