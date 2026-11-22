using System.Text;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Produces the <c>GUI/&lt;Mod&gt;SpriteData.xml</c> file that ships in a
/// Bannerlord module. Byte-identical to SpriteForge's Go output
/// (<c>internal/sprite/xmlout.go::buildXML</c>) and, through it, to the
/// Modding Kit: UTF-8 BOM, XML declaration, CRLF line endings, two-space
/// indent, no trailing newline.
/// </summary>
public static class SpriteDataXmlWriter
{
    public static byte[] Build(
        IReadOnlyList<SpriteCategory> categories,
        IReadOnlyList<SpritePart> parts,
        IReadOnlyList<SpriteEntry> sprites)
    {
        var writer = new XmlStreamWriter();
        writer.RawBytes(new byte[] { 0xEF, 0xBB, 0xBF });
        writer.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.OpenTag("SpriteData");

        writer.OpenTag("SpriteCategories");
        foreach (var category in categories)
        {
            writer.OpenTag("SpriteCategory");
            writer.TextTag("Name", category.Name);
            if (category.Config.AlwaysLoad) writer.Line("<AlwaysLoad />");
            writer.IntTag("SpriteSheetCount", category.SpriteSheetCount);

            for (var sheetIndex = 0; sheetIndex < category.SpriteSheetCount; sheetIndex++)
            {
                var w = sheetIndex < category.Sheets.Count ? category.Sheets[sheetIndex].Width  : 0;
                var h = sheetIndex < category.Sheets.Count ? category.Sheets[sheetIndex].Height : 0;
                writer.Line(
                    $"<SpriteSheetSize ID=\"{sheetIndex + 1}\" Width=\"{w}\" Height=\"{h}\" />");
            }

            writer.CloseTag("SpriteCategory");
        }
        writer.CloseTag("SpriteCategories");

        writer.OpenTag("SpriteParts");
        foreach (var part in parts)
        {
            writer.OpenTag("SpritePart");
            writer.IntTag("SheetID", part.SheetId);
            writer.TextTag("Name", part.Name);
            writer.IntTag("Width", part.Width);
            writer.IntTag("Height", part.Height);
            writer.IntTag("SheetX", part.SheetX);
            writer.IntTag("SheetY", part.SheetY);
            writer.TextTag("CategoryName", part.Category);
            writer.CloseTag("SpritePart");
        }
        writer.CloseTag("SpriteParts");

        writer.OpenTag("Sprites");
        foreach (var sprite in sprites)
        {
            if (sprite.Nine is { } nine)
            {
                writer.OpenTag("NineRegionSprite");
                writer.TextTag("Name", sprite.Name);
                writer.TextTag("SpritePartName", sprite.Part.Name);
                writer.IntTag("LeftWidth", nine.LeftWidth);
                writer.IntTag("RightWidth", nine.RightWidth);
                writer.IntTag("TopHeight", nine.TopHeight);
                writer.IntTag("BottomHeight", nine.BottomHeight);
                writer.CloseTag("NineRegionSprite");
            }
            else
            {
                writer.OpenTag("GenericSprite");
                writer.TextTag("Name", sprite.Name);
                writer.TextTag("SpritePartName", sprite.Part.Name);
                writer.CloseTag("GenericSprite");
            }
        }
        writer.CloseTag("Sprites");

        writer.CloseTag("SpriteData");
        return writer.ToBytes();
    }

    /// <summary>
    /// Appends raw markup matching <c>XmlDocument.Save</c>'s defaults:
    /// CRLF-prefixed lines, two-space indentation per depth level, no
    /// trailing newline.
    /// </summary>
    sealed class XmlStreamWriter
    {
        readonly MemoryStream _buffer = new();
        int _depth;

        public void RawBytes(byte[] bytes) => _buffer.Write(bytes, 0, bytes.Length);

        public void Raw(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _buffer.Write(bytes, 0, bytes.Length);
        }

        public void Line(string content)
        {
            Raw("\r\n");
            for (var i = 0; i < _depth; i++) Raw("  ");
            Raw(content);
        }

        public void OpenTag(string name)  { Line($"<{name}>"); _depth++; }
        public void CloseTag(string name) { _depth--; Line($"</{name}>"); }
        public void TextTag(string name, string value) =>
            Line($"<{name}>{Escape(value)}</{name}>");
        public void IntTag(string name, int value) =>
            Line($"<{name}>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</{name}>");

        public byte[] ToBytes() => _buffer.ToArray();

        static string Escape(string value) => value
            .Replace("&",  "&amp;")
            .Replace("<",  "&lt;")
            .Replace(">",  "&gt;");
    }
}
