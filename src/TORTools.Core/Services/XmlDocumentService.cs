using System.Text;
using System.Xml;
using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and saving XML documents with formatting preservation.
/// </summary>
public class XmlDocumentService : IXmlDocumentService
{
    /// <summary>
    /// Loads an XML document from the specified file path.
    /// Preserves whitespace and detects encoding/BOM.
    /// </summary>
    public XmlDocumentWrapper Load(string filePath)
    {
        // Read raw bytes to detect BOM
        var bytes = File.ReadAllBytes(filePath);
        var hasBom = bytes.Length >= 3 &&
                     bytes[0] == 0xEF &&
                     bytes[1] == 0xBB &&
                     bytes[2] == 0xBF;

        // Detect encoding from XML declaration or default to UTF-8
        var encoding = DetectEncoding(bytes) ?? Encoding.UTF8;

        // Detect original encoding string from XML declaration
        var originalEncodingString = DetectEncodingString(bytes);

        // Read original content for text-based patching
        var originalContent = encoding.GetString(hasBom ? bytes.Skip(3).ToArray() : bytes);

        // Load document preserving whitespace
        var doc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);

        // Detect indentation string
        var indentString = DetectIndentation(doc);

        var wrapper = new XmlDocumentWrapper(doc, filePath, hasBom, encoding, indentString, originalEncodingString);
        wrapper.OriginalContent = originalContent;
        return wrapper;
    }

    /// <summary>
    /// Saves the XML document with configurable attribute formatting.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <param name="filePath">Optional path override.</param>
    /// <param name="compactFormat">If true, write all attributes on single line; if false, each on new line.</param>
    public void Save(XmlDocumentWrapper document, string? filePath = null, bool compactFormat = false)
    {
        var targetPath = filePath ?? document.FilePath;
        var tempPath = targetPath + ".tmp";

        try
        {
            var sb = new StringBuilder();
            var indent = document.IndentString;

            // Write XML declaration
            var decl = document.Document.Declaration;
            if (decl != null)
            {
                var encodingStr = document.OriginalEncodingString ?? "UTF-8";
                sb.AppendLine($"<?xml version=\"{decl.Version}\" encoding=\"{encodingStr}\"?>");
            }

            var root = document.Document.Root;
            if (root != null)
            {
                // Write root element
                sb.AppendLine($"<{root.Name.LocalName}>");

                // Write each entry with appropriate formatting
                foreach (var element in root.Elements())
                {
                    if (compactFormat)
                        WriteElementCompact(sb, element, indent);
                    else
                        WriteElementWithMultiLineAttributes(sb, element, indent);
                }

                sb.Append($"</{root.Name.LocalName}>");
            }

            var content = sb.ToString();

            // Write with proper encoding and BOM
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                if (document.HasBom)
                {
                    byte[] bom = { 0xEF, 0xBB, 0xBF };
                    stream.Write(bom, 0, bom.Length);
                }

                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
            }

            // Atomic replace
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            document.OriginalContent = content;
            document.HasUnsavedChanges = false;
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Writes an element with each attribute on its own line.
    /// </summary>
    private static void WriteElementWithMultiLineAttributes(StringBuilder sb, XElement element, string baseIndent, int depth = 1)
    {
        var indent = string.Concat(Enumerable.Repeat(baseIndent, depth));
        var elementName = element.Name.LocalName;
        var attributes = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var hasTextContent = !string.IsNullOrWhiteSpace(element.Value) && !children.Any();

        // Calculate alignment padding (align under first attribute)
        var alignPad = new string(' ', indent.Length + elementName.Length + 2); // +2 for "< "

        sb.Append(indent);
        sb.Append($"<{elementName}");

        // Write attributes
        for (int i = 0; i < attributes.Count; i++)
        {
            var attr = attributes[i];
            var attrStr = $"{attr.Name.LocalName}=\"{EscapeXmlAttributeValue(attr.Value)}\"";

            if (i == 0)
            {
                sb.Append($" {attrStr}");
            }
            else
            {
                sb.AppendLine();
                sb.Append($"{alignPad}{attrStr}");
            }
        }

        // Close element
        if (children.Any())
        {
            sb.AppendLine(">");

            // Write child elements
            foreach (var child in children)
            {
                WriteElementWithMultiLineAttributes(sb, child, baseIndent, depth + 1);
            }

            sb.AppendLine($"{indent}</{elementName}>");
        }
        else if (hasTextContent)
        {
            sb.AppendLine($">{EscapeXmlText(element.Value)}</{elementName}>");
        }
        else
        {
            sb.AppendLine(" />");
        }
    }

    /// <summary>
    /// Writes an element with all attributes on a single line (compact format).
    /// </summary>
    private static void WriteElementCompact(StringBuilder sb, XElement element, string baseIndent, int depth = 1)
    {
        var indent = string.Concat(Enumerable.Repeat(baseIndent, depth));
        var elementName = element.Name.LocalName;
        var attributes = element.Attributes().ToList();
        var children = element.Elements().ToList();
        var hasTextContent = !string.IsNullOrWhiteSpace(element.Value) && !children.Any();

        sb.Append(indent);
        sb.Append($"<{elementName}");

        // Write all attributes on one line
        foreach (var attr in attributes)
        {
            sb.Append($" {attr.Name.LocalName}=\"{EscapeXmlAttributeValue(attr.Value)}\"");
        }

        // Close element
        if (children.Any())
        {
            sb.AppendLine(">");

            // Write child elements (also compact)
            foreach (var child in children)
            {
                WriteElementCompact(sb, child, baseIndent, depth + 1);
            }

            sb.AppendLine($"{indent}</{elementName}>");
        }
        else if (hasTextContent)
        {
            sb.AppendLine($">{EscapeXmlText(element.Value)}</{elementName}>");
        }
        else
        {
            sb.AppendLine(" />");
        }
    }

    /// <summary>
    /// Escapes text content for XML.
    /// </summary>
    private static string EscapeXmlText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    /// <summary>
    /// Patches attribute values in the original content for a specific element.
    /// Preserves the original multi-line attribute formatting.
    /// </summary>
    private static string PatchElementAttributes(string content, XElement element)
    {
        // Find the element's ID or StringID to locate it in the text
        var idAttr = element.Attribute("id") ?? element.Attribute("StringID") ?? element.Attribute("ItemTraitStringId");
        if (idAttr == null)
        {
            Console.WriteLine($"[Patch] Element {element.Name.LocalName} has no id attribute, skipping");
            return content;
        }

        var idValue = idAttr.Value;
        var idName = idAttr.Name.LocalName;

        // Find this element in the content by its ID attribute
        // Pattern: <ElementName ... idName="idValue" ... > or />
        var elementName = element.Name.LocalName;

        // Look for the element start tag containing this ID
        var searchPattern = $"<{elementName}[^>]*{idName}\\s*=\\s*\"{System.Text.RegularExpressions.Regex.Escape(idValue)}\"";
        var match = System.Text.RegularExpressions.Regex.Match(content, searchPattern, System.Text.RegularExpressions.RegexOptions.Singleline);

        if (!match.Success)
        {
            Console.WriteLine($"[Patch] Could not find element {elementName} with {idName}={idValue} in content");
            return content;
        }

        // Find the full element tag (up to > or />)
        var startIndex = match.Index;
        var endIndex = content.IndexOf('>', startIndex);
        if (endIndex < 0) return content;

        // Check if it's a self-closing tag
        var isSelfClosing = content[endIndex - 1] == '/';
        var originalTag = content.Substring(startIndex, endIndex - startIndex + 1);

        // For each attribute in the XElement, update its value in the original tag
        var patchedTag = originalTag;
        foreach (var attr in element.Attributes())
        {
            var attrName = attr.Name.LocalName;
            var newValue = attr.Value;

            // Pattern to find this attribute and replace its value
            // Handles: attrName="value" or attrName = "value" with possible newlines
            var attrPattern = $@"({attrName}\s*=\s*"")([^""]*)("")";
            patchedTag = System.Text.RegularExpressions.Regex.Replace(
                patchedTag,
                attrPattern,
                m => m.Groups[1].Value + EscapeXmlAttributeValue(newValue) + m.Groups[3].Value);
        }

        // Replace the original tag with the patched version
        if (patchedTag != originalTag)
        {
            Console.WriteLine($"[Patch] Patched {elementName} {idName}={idValue}");
            content = content.Substring(0, startIndex) + patchedTag + content.Substring(endIndex + 1);
        }

        return content;
    }

    /// <summary>
    /// Escapes special characters for XML attribute values.
    /// XElement.Value gives unescaped values, so we must escape them for XML text.
    /// Order matters: & must be escaped first.
    /// </summary>
    private static string EscapeXmlAttributeValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    /// <summary>
    /// Gets all top-level entries from the document.
    /// </summary>
    public IReadOnlyList<XmlEntry> GetEntries(XmlDocumentWrapper document)
    {
        var root = document.Document.Root;
        if (root == null)
            return Array.Empty<XmlEntry>();

        return root.Elements()
            .Select(e => new XmlEntry(e))
            .ToList();
    }

    /// <summary>
    /// Adds a new entry to the document.
    /// </summary>
    public XmlEntry AddEntry(XmlDocumentWrapper document, XmlEntry? template = null)
    {
        var root = document.Document.Root;
        if (root == null)
            throw new InvalidOperationException("Document has no root element");

        XElement newElement;

        if (template != null)
        {
            // Clone the template element
            newElement = new XElement(template.OriginalElement);

            // Generate a new unique ID
            var idAttr = newElement.Attribute("id");
            if (idAttr != null)
            {
                idAttr.Value = GenerateNewId(document, idAttr.Value);
            }

            // Update name localization key if present
            var nameAttr = newElement.Attribute("name");
            if (nameAttr != null && idAttr != null)
            {
                var (key, text) = LocalizationHelper.Unwrap(nameAttr.Value);
                if (key != null)
                {
                    nameAttr.Value = LocalizationHelper.Wrap(
                        LocalizationHelper.GenerateKey(idAttr.Value),
                        text + " (Copy)");
                }
            }
        }
        else
        {
            // Create minimal new element
            newElement = new XElement(document.EntryElementName);
            newElement.SetAttributeValue("id", GenerateNewId(document, "new_entry"));
        }

        // Add proper indentation before the element
        var lastElement = root.Elements().LastOrDefault();
        if (lastElement != null)
        {
            // Copy whitespace pattern from existing elements
            var previousNode = lastElement.PreviousNode;
            if (previousNode is XText whitespace)
            {
                root.Add(new XText(whitespace.Value));
            }
        }
        else
        {
            root.Add(new XText("\n" + document.IndentString));
        }

        root.Add(newElement);
        document.HasUnsavedChanges = true;

        return new XmlEntry(newElement);
    }

    /// <summary>
    /// Removes an entry from the document.
    /// </summary>
    public void RemoveEntry(XmlDocumentWrapper document, XmlEntry entry)
    {
        // Also remove preceding whitespace to keep formatting clean
        var previousNode = entry.OriginalElement.PreviousNode;
        if (previousNode is XText)
        {
            previousNode.Remove();
        }

        entry.OriginalElement.Remove();
        document.HasUnsavedChanges = true;
    }

    /// <summary>
    /// Duplicates an existing entry with a new ID.
    /// </summary>
    public XmlEntry DuplicateEntry(XmlDocumentWrapper document, XmlEntry original)
    {
        return AddEntry(document, original);
    }

    private static Encoding? DetectEncoding(byte[] bytes)
    {
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Try to read encoding from XML declaration
        var text = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 100));
        if (text.Contains("encoding=\"UTF-8\"", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("encoding='UTF-8'", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;

        return null;
    }

    private static string? DetectEncodingString(byte[] bytes)
    {
        // Extract the exact encoding string from the XML declaration
        var text = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));

        // Look for encoding="..." or encoding='...'
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"encoding\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DetectIndentation(XDocument doc)
    {
        var root = doc.Root;
        if (root == null)
            return "  "; // Default to 2 spaces

        // Look at whitespace before first child element
        var firstChild = root.Elements().FirstOrDefault();
        if (firstChild == null)
            return "  ";

        var previousNode = firstChild.PreviousNode;
        if (previousNode is XText whitespace)
        {
            var text = whitespace.Value;
            // Find the indentation after the last newline
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline >= 0)
            {
                var indent = text[(lastNewline + 1)..];
                if (!string.IsNullOrEmpty(indent))
                    return indent;
            }
        }

        return "  "; // Default to 2 spaces
    }

    private static string GenerateNewId(XmlDocumentWrapper document, string baseId)
    {
        var existingIds = document.Document.Root?.Elements()
            .Select(e => e.Attribute("id")?.Value)
            .Where(id => id != null)
            .ToHashSet() ?? new HashSet<string?>();

        // Try base_copy, base_copy_1, base_copy_2, etc.
        var newId = baseId + "_copy";
        var counter = 1;

        while (existingIds.Contains(newId))
        {
            newId = $"{baseId}_copy_{counter}";
            counter++;
        }

        return newId;
    }
}
