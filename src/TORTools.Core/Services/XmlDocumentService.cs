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

        // Load document preserving whitespace
        var doc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);

        // Detect indentation string
        var indentString = DetectIndentation(doc);

        return new XmlDocumentWrapper(doc, filePath, hasBom, encoding, indentString, originalEncodingString);
    }

    /// <summary>
    /// Saves the XML document with minimal formatting changes.
    /// </summary>
    public void Save(XmlDocumentWrapper document, string? filePath = null)
    {
        var targetPath = filePath ?? document.FilePath;
        var tempPath = targetPath + ".tmp";

        try
        {
            var encoding = document.HasBom
                ? new UTF8Encoding(true)  // UTF-8 with BOM
                : new UTF8Encoding(false); // UTF-8 without BOM

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                // Write BOM explicitly if original had one
                if (document.HasBom)
                {
                    byte[] bom = { 0xEF, 0xBB, 0xBF };
                    stream.Write(bom, 0, bom.Length);
                }

                // Use UTF-8 without BOM for the writer (we already wrote BOM manually)
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));

                // Write XML declaration manually to preserve original casing
                var decl = document.Document.Declaration;
                if (decl != null)
                {
                    // Preserve original encoding string if available, otherwise use UTF-8
                    var encodingStr = document.OriginalEncodingString ?? "UTF-8";
                    writer.Write($"<?xml version=\"{decl.Version}\" encoding=\"{encodingStr}\"?>");
                }

                // Write the rest of the document (Nodes() doesn't include the declaration)
                // Don't add any extra newlines - preserve exactly what was there
                foreach (var node in document.Document.Nodes())
                {
                    writer.Write(node.ToString(SaveOptions.DisableFormatting));
                }
            }

            // Atomic replace: delete original, rename temp
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            document.HasUnsavedChanges = false;
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
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
