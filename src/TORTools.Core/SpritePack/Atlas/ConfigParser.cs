using System.Xml;

namespace TORTools.Core.SpritePack.Atlas;

/// <summary>
/// Reads a module's sprite-category configuration out of
/// <c>GUI/SpriteParts/Config.xml</c> (with a fallback to the misplaced
/// <c>GUI/Config.xml</c>, same discipline as SpriteForge's
/// <c>loadConfigs</c>).
/// </summary>
public static class ConfigParser
{
    /// <summary>
    /// Returns per-category configs keyed by category name, in document
    /// order. Duplicate category names throw
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static Dictionary<string, CategoryConfig> ParseFile(string path)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.Load(path);
        return Parse(doc);
    }

    public static Dictionary<string, CategoryConfig> Parse(XmlDocument doc)
    {
        var configs = new Dictionary<string, CategoryConfig>(StringComparer.Ordinal);
        if (doc.DocumentElement is not { } root) return configs;

        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is not XmlElement element) continue;

            var nameAttr = element.Attributes?["Name"]?.Value
                ?? throw new InvalidDataException(
                    $"<{element.LocalName}> without Name attribute");

            if (configs.ContainsKey(nameAttr))
                throw new InvalidDataException($"duplicate category '{nameAttr}'");

            configs[nameAttr] = ParseCategory(element, nameAttr);
        }
        return configs;
    }

    static CategoryConfig ParseCategory(XmlElement element, string name)
    {
        var unique = false;
        var singleChannel = false;
        var noAlpha = false;
        var alwaysLoad = false;
        var compress = false;
        var edge = 0;
        var hasEdge = false;

        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is not XmlElement childElement) continue;
            switch (childElement.LocalName)
            {
                case "PackAllSpritesToUniqueTextures": unique = true; break;
                case "SingleChannel":                  singleChannel = true; break;
                case "NoAlphaChannel":                 noAlpha = true; break;
                case "AlwaysLoad":                     alwaysLoad = true; break;
                case "Compress":                       compress = true; break;
                case "EdgeSize":
                    var valueAttr = childElement.Attributes?["Value"]?.Value
                        ?? throw new InvalidDataException(
                            $"category {name}: EdgeSize without Value");
                    if (!int.TryParse(valueAttr.Trim(), out var edgeValue))
                        throw new InvalidDataException(
                            $"category {name}: bad EdgeSize {valueAttr}");
                    if (edgeValue < 0)
                        throw new InvalidDataException(
                            $"category {name}: negative EdgeSize {edgeValue}");
                    edge = edgeValue;
                    hasEdge = true;
                    break;
            }
        }

        return new CategoryConfig
        {
            PackAllSpritesToUniqueTextures = unique,
            SingleChannel = singleChannel,
            NoAlphaChannel = noAlpha,
            AlwaysLoad = alwaysLoad,
            Compress = compress,
            EdgeSize = edge,
            HasEdge = hasEdge,
        };
    }
}
