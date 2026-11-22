using System.Text;

namespace TORTools.Core.SpritePack.Tpac;

/// <summary>
/// Low-level readers and writers for the Bannerlord TPAC binary format.
/// GUIDs round-trip via <see cref="Guid.ToByteArray"/> — the format writes them
/// in the same mixed-endian layout .NET uses natively.
/// </summary>
public static class BinaryUtils
{
    /// <summary>u32 length prefix, then UTF-8 bytes (no terminator).</summary>
    public static string ReadSizedString(this BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    public static void WriteSizedString(this BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>u32 count, then <paramref name="count"/> sized strings.</summary>
    public static List<string> ReadStringList(this BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var list = new List<string>(count);
        for (var i = 0; i < count; i++) list.Add(reader.ReadSizedString());
        return list;
    }

    public static void WriteStringList(this BinaryWriter writer, IReadOnlyList<string> list)
    {
        writer.Write(list.Count);
        foreach (var s in list) writer.WriteSizedString(s);
    }

    public static Guid ReadGuid(this BinaryReader reader) => new(reader.ReadBytes(16));

    public static void WriteGuid(this BinaryWriter writer, Guid guid) => writer.Write(guid.ToByteArray());
}
