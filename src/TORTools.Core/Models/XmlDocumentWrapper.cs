using System.Text;
using System.Xml.Linq;

namespace TORTools.Core.Models;

/// <summary>
/// Wraps an XDocument with metadata needed for formatting-preserving saves.
/// </summary>
public class XmlDocumentWrapper
{
    /// <summary>
    /// The underlying XDocument.
    /// </summary>
    public XDocument Document { get; }

    /// <summary>
    /// The file path this document was loaded from.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The name of the root element (e.g., "Items", "SPCultures", "Heroes").
    /// </summary>
    public string RootElementName => Document.Root?.Name.LocalName ?? "";

    /// <summary>
    /// The name of the entry elements (e.g., "Item", "Culture", "Hero").
    /// Inferred from the first child of the root element.
    /// </summary>
    public string EntryElementName { get; }

    /// <summary>
    /// Whether the original file had a UTF-8 BOM.
    /// </summary>
    public bool HasBom { get; }

    /// <summary>
    /// The original encoding of the file.
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// Whether the document has unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges { get; set; }

    /// <summary>
    /// The indentation string used in this document (detected from content).
    /// </summary>
    public string IndentString { get; }

    /// <summary>
    /// The original encoding string as it appeared in the XML declaration (e.g., "UTF-8").
    /// </summary>
    public string? OriginalEncodingString { get; }

    /// <summary>
    /// The original raw content of the file (for text-based patching).
    /// </summary>
    public string OriginalContent { get; set; } = "";

    public XmlDocumentWrapper(
        XDocument document,
        string filePath,
        bool hasBom,
        Encoding encoding,
        string indentString,
        string? originalEncodingString = null)
    {
        Document = document;
        FilePath = filePath;
        HasBom = hasBom;
        Encoding = encoding;
        IndentString = indentString;
        OriginalEncodingString = originalEncodingString ?? "UTF-8";

        // Infer entry element name from first child
        EntryElementName = Document.Root?.Elements().FirstOrDefault()?.Name.LocalName ?? "Entry";
    }
}
