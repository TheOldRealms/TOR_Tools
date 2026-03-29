using System.Collections.ObjectModel;
using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that adds a new row to the document.
/// </summary>
public class AddRowCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly ObservableCollection<XmlEntry> _entries;
    private readonly int _insertIndex;
    private XmlEntry? _addedEntry;
    private XText? _addedWhitespace;

    public string Description => "Add Row";

    public AddRowCommand(
        XmlDocumentWrapper document,
        ObservableCollection<XmlEntry> entries,
        int insertIndex = -1)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _insertIndex = insertIndex < 0 ? entries.Count : Math.Min(insertIndex, entries.Count);
    }

    public void Execute()
    {
        var root = _document.Document.Root;
        if (root == null) return;

        // Create a new element with default attributes
        var newElement = new XElement(_document.EntryElementName);
        var newId = GenerateUniqueId();
        newElement.SetAttributeValue("id", newId);

        // Determine insertion point in XML
        XNode? insertAfter = null;
        if (_insertIndex > 0 && _insertIndex <= _entries.Count)
        {
            insertAfter = _entries[_insertIndex - 1].OriginalElement;
        }

        // Add whitespace before the element
        var whitespaceText = "\n" + _document.IndentString;
        _addedWhitespace = new XText(whitespaceText);

        if (insertAfter != null)
        {
            insertAfter.AddAfterSelf(_addedWhitespace);
            _addedWhitespace.AddAfterSelf(newElement);
        }
        else
        {
            // Insert at beginning
            var firstElement = root.Elements().FirstOrDefault();
            if (firstElement != null)
            {
                firstElement.AddBeforeSelf(_addedWhitespace);
                _addedWhitespace.AddAfterSelf(newElement);
            }
            else
            {
                root.Add(_addedWhitespace);
                root.Add(newElement);
            }
        }

        _addedEntry = new XmlEntry(newElement);
        _entries.Insert(_insertIndex, _addedEntry);
        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        if (_addedEntry == null) return;

        _entries.Remove(_addedEntry);
        _addedWhitespace?.Remove();
        _addedEntry.OriginalElement.Remove();
        _addedEntry = null;
        _addedWhitespace = null;
        _document.HasUnsavedChanges = true;
    }

    private string GenerateUniqueId()
    {
        var existingIds = _entries
            .Select(e => e.Id)
            .Where(id => id != null)
            .ToHashSet();

        var counter = 1;
        string newId;
        do
        {
            newId = $"new_entry_{counter}";
            counter++;
        } while (existingIds.Contains(newId));

        return newId;
    }
}
