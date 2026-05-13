using TORTools.Core.Models;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and saving XML documents with formatting preservation.
/// </summary>
public interface IXmlDocumentService
{
    /// <summary>
    /// Loads an XML document from the specified file path.
    /// </summary>
    XmlDocumentWrapper Load(string filePath);

    /// <summary>
    /// Saves the XML document to the specified file path with minimal formatting changes.
    /// Uses atomic write (temp file + rename) for safety.
    /// </summary>
    /// <param name="document">The document to save.</param>
    /// <param name="filePath">Optional path override.</param>
    /// <param name="compactFormat">If true, write attributes on single line; if false, each on new line.</param>
    /// <param name="groupByField">Optional field to group entries by, with comments between groups.</param>
    /// <param name="excludeFields">Optional fields to exclude from writing (e.g., linked fields stored elsewhere).</param>
    void Save(XmlDocumentWrapper document, string? filePath = null, bool compactFormat = false, string? groupByField = null, HashSet<string>? excludeFields = null);

    /// <summary>
    /// Saves entries with category/subcategory grouping from XmlEntry values (for linked fields).
    /// Writes category comments before each group, and subcategory comments using " - " separator.
    /// </summary>
    /// <param name="document">The document template (for encoding/indent settings).</param>
    /// <param name="entries">The entries to save with their category/subcategory values.</param>
    /// <param name="filePath">The file path to save to.</param>
    /// <param name="rootElement">The root element name.</param>
    /// <param name="compactFormat">If true, write attributes on single line.</param>
    /// <param name="excludeFields">Optional fields to exclude from writing.</param>
    void SaveWithCategoryComments(
        XmlDocumentWrapper document,
        IReadOnlyList<XmlEntry> entries,
        string filePath,
        string rootElement,
        bool compactFormat = true,
        HashSet<string>? excludeFields = null);

    /// <summary>
    /// Gets all top-level entries from the document.
    /// </summary>
    IReadOnlyList<XmlEntry> GetEntries(XmlDocumentWrapper document);

    /// <summary>
    /// Adds a new entry to the document, copying structure from a template entry.
    /// </summary>
    XmlEntry AddEntry(XmlDocumentWrapper document, XmlEntry? template = null);

    /// <summary>
    /// Removes an entry from the document.
    /// </summary>
    void RemoveEntry(XmlDocumentWrapper document, XmlEntry entry);

    /// <summary>
    /// Duplicates an existing entry with a new ID.
    /// </summary>
    XmlEntry DuplicateEntry(XmlDocumentWrapper document, XmlEntry original);
}
