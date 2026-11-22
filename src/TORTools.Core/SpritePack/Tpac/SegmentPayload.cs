using K4os.Compression.LZ4;

namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Compresses a raw payload for storage in a <see cref="DataSegment"/>, using
/// the same rule the engine's own writer uses: try LZ4 high-compression,
/// keep it when it shrinks the blob, fall back to raw bytes otherwise. See
/// the comment at bannerlord-editor-free-sdk sdk/tw_formats.py::pack_rdc_exact.
/// </summary>
public static class SegmentPayload
{
    public static void Encode(DataSegment segment, byte[] raw, bool tryLz4 = true)
    {
        segment.ActualSize = (ulong)raw.Length;

        if (!tryLz4)
        {
            segment.StoredBytes = raw;
            segment.StoredSize = segment.ActualSize;
            segment.StorageFormat = SegmentStorageFormat.Raw;
            return;
        }

        var maxLen = LZ4Codec.MaximumOutputSize(raw.Length);
        var buffer = new byte[maxLen];
        var written = LZ4Codec.Encode(raw, 0, raw.Length, buffer, 0, maxLen, LZ4Level.L12_MAX);

        if (written > 0 && written < raw.Length)
        {
            var compressed = new byte[written];
            Buffer.BlockCopy(buffer, 0, compressed, 0, written);
            segment.StoredBytes = compressed;
            segment.StoredSize = (ulong)written;
            segment.StorageFormat = SegmentStorageFormat.Lz4;
        }
        else
        {
            segment.StoredBytes = raw;
            segment.StoredSize = segment.ActualSize;
            segment.StorageFormat = SegmentStorageFormat.Raw;
        }
    }

    public static byte[] Decode(DataSegment segment)
    {
        return segment.StorageFormat switch
        {
            SegmentStorageFormat.Raw => segment.StoredBytes,
            SegmentStorageFormat.Lz4 => DecodeLz4(segment),
            _ => throw new InvalidDataException($"Unknown storage format: {segment.StorageFormat}"),
        };
    }

    static byte[] DecodeLz4(DataSegment segment)
    {
        var output = new byte[segment.ActualSize];
        var decoded = LZ4Codec.Decode(segment.StoredBytes, 0, segment.StoredBytes.Length,
            output, 0, output.Length);
        if (decoded != output.Length)
            throw new InvalidDataException(
                $"LZ4 decode produced {decoded} bytes, expected {segment.ActualSize}.");
        return output;
    }
}
