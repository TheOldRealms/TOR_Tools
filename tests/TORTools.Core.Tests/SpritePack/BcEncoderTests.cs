using TORTools.Core.SpritePack.Atlas;

namespace TORTools.Core.Tests.SpritePack;

public class BcEncoderTests
{
    [Fact]
    public void EncodeRgba8_passes_bytes_through_unchanged()
    {
        var input = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        BcEncoder.EncodeRgba8(input).Should().BeSameAs(input);
    }

    [Fact]
    public void EncodeDxt5_produces_the_expected_block_count()
    {
        // 4×4 = 1 block = 16 bytes (DXT5/BC3 block size).
        var rgba = new byte[4 * 4 * 4];
        new Random(42).NextBytes(rgba);
        var bc3 = BcEncoder.EncodeDxt5(rgba, 4, 4);
        bc3.Should().HaveCount(16, "a single 4×4 BC3 block is 16 bytes");

        // 8×8 = 4 blocks = 64 bytes.
        var rgba88 = new byte[8 * 8 * 4];
        new Random(7).NextBytes(rgba88);
        var bc3_88 = BcEncoder.EncodeDxt5(rgba88, 8, 8);
        bc3_88.Should().HaveCount(64);
    }

    [Fact]
    public void EncodeDxt5_rejects_non_multiple_of_4_dimensions()
    {
        var rgba = new byte[5 * 4 * 4];
        var act = () => BcEncoder.EncodeDxt5(rgba, 5, 4);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*divisible by 4*");
    }

    [Fact]
    public void EncodeDxt5_rejects_mismatched_buffer_size()
    {
        var rgba = new byte[32]; // declares 8 pixels, but we claim a 4×4 (16 px) image
        var act = () => BcEncoder.EncodeDxt5(rgba, 4, 4);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*expected 64*");
    }
}
