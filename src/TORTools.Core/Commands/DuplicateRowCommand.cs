using System.Collections.ObjectModel;
using System.Xml.Linq;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that duplicates a row with a new unique ID.
/// </summary>
public class DuplicateRowCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly ObservableCollection<XmlEntry> _entries;
    private readonly XmlEntry _originalEntry;
    private readonly int _insertIndex;
    private XmlEntry? _duplicatedEntry;
    private XText? _addedWhitespace;

    public string Description => $"Duplicate {_originalEntry.Id ?? "Row"}";

    public DuplicateRowCommand(
        XmlDocumentWrapper document,
        ObservableCollection<XmlEntry> entries,
        XmlEntry originalEntry)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _originalEntry = originalEntry ?? throw new ArgumentNullException(nameof(originalEntry));
        _insertIndex = entries.IndexOf(originalEntry) + 1;
    }

    public void Execute()
    {
        var root = _document.Document.Root;
        if (root == null) return;

        // Clone the original element
        var newElement = new XElement(_originalEntry.OriginalElement);

        // Generate a new unique ID
        var originalId = newElement.Attribute("id")?.Value ?? "entry";
        var newId = GenerateUniqueId(originalId);
        newElement.SetAttributeValue("id", newId);

        // Update name localization key if present
        var nameAttr = newElement.Attribute("name");
        if (nameAttr != null)
        {
            var (key, text) = LocalizationHelper.Unwrap(nameAttr.Value);
            if (key != null)
            {
                nameAttr.Value = LocalizationHelper.Wrap(
                    LocalizationHelper.GenerateKey(newId),
                    text + " (Copy)");
            }
            else
            {
                nameAttr.Value = text + " (Copy)";
            }
        }

        // Insert after the original
        var whitespaceText = "\n" + _document.IndentString;
        _addedWhitespace = new XText(whitespaceText);

        _originalEntry.OriginalElement.AddAfterSelf(_addedWhitespace);
        _addedWhitespace.AddAfterSelf(newElement);

        _duplicatedEntry = new XmlEntry(newElement);
        _entries.Insert(_insertIndex, _duplicatedEntry);
        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        if (_duplicatedEntry == null) return;

        _entries.Remove(_duplicatedEntry);
        _addedWhitespace?.Remove();
        _duplicatedEntry.OriginalElement.Remove();
        _duplicatedEntry = null;
        _addedWhitespace = null;
        _document.HasUnsavedChanges = true;
    }

    private string GenerateUniqueId(string baseId)
    {
        var existingIds = _entries
            .Select(e => e.Id)
            .Where(id => id != null)
            .ToHashSet();

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
