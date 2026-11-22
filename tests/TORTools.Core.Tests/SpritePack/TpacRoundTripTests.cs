using System.Security.Cryptography;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// Round-trip the real TOR_Armory ability-icons TPAC through our reader +
/// writer and SHA-compare the input vs output. A passing test proves we
/// understand the Modding-Kit binary layout well enough to re-emit it
/// byte-identically for every field the game sees.
/// </summary>
public class TpacRoundTripTests
{
    static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SpritePack", "Fixtures", name);

    [Fact]
    public void Armory_ability_icons_round_trips_byte_identical()
    {
        var path = FixturePath("ui_abilityicons_1_tex.tpac");

        var original = File.ReadAllBytes(path);
        var parsed = TpacReader.Read(path);

        using var buffer = new MemoryStream();
        TpacWriter.Write(buffer, parsed);
        var written = buffer.ToArray();

        written.Length.Should().Be(original.Length);
        Sha256(written).Should().Be(Sha256(original));
    }

    [Fact]
    public void Round_tripped_file_reparses_into_the_same_shape()
    {
        var path = FixturePath("ui_abilityicons_1_tex.tpac");

        var first = TpacReader.Read(path);
        using var buffer = new MemoryStream();
        TpacWriter.Write(buffer, first);
        buffer.Position = 0;
        var second = TpacReader.Read(buffer);

        second.PackageGuid.Should().Be(first.PackageGuid);
        second.Assets.Should().HaveCount(first.Assets.Count);

        var firstAsset = first.Assets.Single();
        var secondAsset = second.Assets.Single();
        secondAsset.Name.Should().Be(firstAsset.Name);
        secondAsset.Guid.Should().Be(firstAsset.Guid);
        secondAsset.MetadataChecksum.Should().Be(firstAsset.MetadataChecksum);
        secondAsset.Segments.Should().HaveCount(firstAsset.Segments.Count);

        var firstTex = firstAsset.Texture!;
        var secondTex = secondAsset.Texture!;
        secondTex.Width.Should().Be(firstTex.Width);
        secondTex.Height.Should().Be(firstTex.Height);
        secondTex.Format.Should().Be(firstTex.Format);
        secondTex.Flags.Should().BeEquivalentTo(firstTex.Flags);
    }

    [Fact]
    public void Armory_ability_icons_has_the_expected_segment_shape()
    {
        var file = TpacReader.Read(FixturePath("ui_abilityicons_1_tex.tpac"));
        file.PackageGuid.Should().Be(Guid.Parse("6f6a0808-69d1-4a34-b175-286ebd86e39f"));

        var asset = file.Assets.Single();
        asset.TypeGuid.Should().Be(TpacGuids.Texture);
        asset.Name.Should().Be("ui_abilityicons_1");

        var tex = asset.Texture!;
        tex.Width.Should().Be(4096u);
        tex.Height.Should().Be(4096u);
        tex.Format.Should().Be("DXT5");
        tex.Flags.Should().ContainInOrder("dont_degrade", "dont_delay_loading");
        tex.SystemFlags.Should().ContainSingle(x => x == "has_alpha");

        asset.Segments.Should().HaveCount(2);
        asset.Segments[0].TypeGuid.Should().Be(TpacGuids.TexSettings);
        asset.Segments[1].TypeGuid.Should().Be(TpacGuids.TexSrcInfo);
    }

    static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
