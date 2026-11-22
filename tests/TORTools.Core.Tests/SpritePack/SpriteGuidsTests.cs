using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

public class SpriteGuidsTests
{
    [Fact]
    public void AssetGuid_matches_SpriteForge_Go_output_for_SpriteTest()
    {
        // Oracle from running the Go sprite-pack binary on
        // examples/SpriteTest/ and reading `spriteforge inspect`:
        //   module="SpriteTest" name="ui_spritetest_1"
        //   guid=3b780bdc-de83-5635-84ba-fe49cf40b3c0
        var guid = SpriteGuids.AssetGuid("SpriteTest", "ui_spritetest_1");
        guid.Should().Be(Guid.Parse("3b780bdc-de83-5635-84ba-fe49cf40b3c0"));
    }

    [Fact]
    public void FixedPackageGuid_matches_SpriteForge_constant()
    {
        SpriteGuids.FixedPackageGuid
            .Should().Be(Guid.Parse("807e3d8d-e501-499b-a1d3-22ae3f4e64f3"));
    }

    [Fact]
    public void UuidV5_against_a_textbook_vector()
    {
        // The DNS namespace from RFC 4122 Appendix C, which lists the UUIDv5
        // of the name "www.example.com" under the DNS namespace as
        // 2ed6657d-e927-568b-95e1-2665a8aea6a2.
        var dnsNs = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        SpriteGuids.UuidV5(dnsNs, "www.example.com")
            .Should().Be(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"));
    }
}
