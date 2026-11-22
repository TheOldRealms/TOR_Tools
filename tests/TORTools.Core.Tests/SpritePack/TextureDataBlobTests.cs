using System.Text;
using TORTools.Core.SpritePack.Tpac;

namespace TORTools.Core.Tests.SpritePack;

/// <summary>
/// Validate the TEX_SETTINGS + TEX_SRCINFO blob builders against the real
/// data segments inside TOR_Armory's ui_abilityicons_1_tex.tpac. If our
/// builders produce the same bytes as the Modding Kit did, we have strong
/// confidence Task 8's atlas builder can slot them straight into a .tpac
/// that the game will accept.
/// </summary>
public class TextureDataBlobTests
{
    static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SpritePack", "Fixtures", name);

    [Fact]
    public void BuildSettings_reproduces_Armory_TEX_SETTINGS_bytes()
    {
        var file = TpacReader.Read(FixturePath("ui_abilityicons_1_tex.tpac"));
        var settingsSeg = file.Assets.Single().Segments[0];
        settingsSeg.TypeGuid.Should().Be(TpacGuids.TexSettings);

        var armoryRaw = SegmentPayload.Decode(settingsSeg);
        armoryRaw.Length.Should().Be((int)settingsSeg.ActualSize);

        // Parse the fields so we assert on them before comparing bytes.
        using var ms = new MemoryStream(armoryRaw);
        using var reader = new BinaryReader(ms);
        var usage = reader.ReadSizedString();
        var doNotCompress = reader.ReadBoolean();
        var noMips = reader.ReadBoolean();
        var tail = reader.ReadBytes((int)(ms.Length - ms.Position));

        usage.Should().Be("albedo");
        // The two byte-flags are a known-opaque pair; the editor-free-sdk
        // comment at sdk/tpac.py:951 labels them "do_not_compress" and
        // "no_mips" but admits "which byte is which is not pinned down".
        // Armory ships 0x01 / 0x00, keep that as the observed oracle.
        doNotCompress.Should().BeTrue();
        noMips.Should().BeFalse();
        tail.Should().OnlyContain(b => b == 0);
        tail.Length.Should().Be(TextureDataBlobs.SettingsFixedPadding);

        var rebuilt = TextureDataBlobs.BuildSettings(usage, doNotCompress, noMips);
        rebuilt.Should().Equal(armoryRaw);
    }

    [Fact]
    public void BuildSrcInfo_reproduces_Armory_TEX_SRCINFO_bytes()
    {
        var file = TpacReader.Read(FixturePath("ui_abilityicons_1_tex.tpac"));
        var srcSeg = file.Assets.Single().Segments[1];
        srcSeg.TypeGuid.Should().Be(TpacGuids.TexSrcInfo);

        var armoryRaw = SegmentPayload.Decode(srcSeg);
        armoryRaw.Length.Should().Be((int)srcSeg.ActualSize);

        using var ms = new MemoryStream(armoryRaw);
        using var reader = new BinaryReader(ms);
        var width = reader.ReadUInt32();
        var height = reader.ReadUInt32();
        var depth = reader.ReadUInt32();
        var array = reader.ReadUInt32();
        var mips = reader.ReadUInt32();
        var format = reader.ReadSizedString();
        var sourceSize = reader.ReadUInt64();

        width.Should().Be(4096);
        height.Should().Be(4096);
        depth.Should().Be(1);
        array.Should().Be(1);
        mips.Should().Be(1);
        format.Should().Be("B8G8R8A8_UNORM");
        sourceSize.Should().BeGreaterThan(0ul);

        var rebuilt = TextureDataBlobs.BuildSrcInfo(width, height, format, sourceSize,
            depth: depth, array: array, mips: mips);
        rebuilt.Should().Equal(armoryRaw);
    }

    [Fact]
    public void SegmentPayload_lz4_round_trip_restores_exact_bytes()
    {
        // Pattern-heavy data so LZ4 definitely shrinks it.
        var raw = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("spritepack-", 256)));

        var segment = new DataSegment { TypeGuid = TpacGuids.TexPixels };
        SegmentPayload.Encode(segment, raw);
        segment.StorageFormat.Should().Be(SegmentStorageFormat.Lz4);
        segment.ActualSize.Should().Be((ulong)raw.Length);
        segment.StoredSize.Should().BeLessThan(segment.ActualSize);

        SegmentPayload.Decode(segment).Should().Equal(raw);
    }

    [Fact]
    public void SegmentPayload_keeps_raw_when_lz4_does_not_shrink()
    {
        // Random bytes don't compress — writer should fall back to raw.
        var raw = new byte[64];
        new Random(42).NextBytes(raw);

        var segment = new DataSegment();
        SegmentPayload.Encode(segment, raw);
        segment.StorageFormat.Should().Be(SegmentStorageFormat.Raw);
        segment.StoredSize.Should().Be((ulong)raw.Length);
        segment.StoredBytes.Should().Equal(raw);
        SegmentPayload.Decode(segment).Should().Equal(raw);
    }
}
