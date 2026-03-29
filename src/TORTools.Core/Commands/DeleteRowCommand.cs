using System.Collections.ObjectModel;
using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that deletes a row from the document.
/// </summary>
public class DeleteRowCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly ObservableCollection<XmlEntry> _entries;
    private readonly XmlEntry _entry;
    private readonly int _originalIndex;
    private XNode? _precedingWhitespace;
    private XNode? _insertAfter;

    public string Description => $"Delete {_entry.Id ?? "Row"}";

    public DeleteRowCommand(
        XmlDocumentWrapper document,
        ObservableCollection<XmlEntry> entries,
        XmlEntry entry)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _originalIndex = entries.IndexOf(entry);
    }

    public void Execute()
    {
        // Store the preceding whitespace for undo
        var prevNode = _entry.OriginalElement.PreviousNode;
        if (prevNode is XText whitespace)
        {
            _precedingWhitespace = whitespace;
        }

        // Store what element to insert after when undoing
        _insertAfter = _precedingWhitespace?.PreviousNode ?? _entry.OriginalElement.PreviousNode;

        // Remove whitespace and element
        _precedingWhitespace?.Remove();
        _entry.OriginalElement.Remove();
        _entries.Remove(_entry);
        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        var root = _document.Document.Root;
        if (root == null) return;

        // Re-insert at original position
        if (_insertAfter != null)
        {
            if (_precedingWhitespace != null)
            {
                _insertAfter.AddAfterSelf(_precedingWhitespace);
                _precedingWhitespace.AddAfterSelf(_entry.OriginalElement);
            }
            else
            {
                _insertAfter.AddAfterSelf(_entry.OriginalElement);
            }
        }
        else
        {
            // Insert at beginning
            var firstNode = root.FirstNode;
            if (firstNode != null)
            {
                if (_precedingWhitespace != null)
                {
                    firstNode.AddBeforeSelf(_precedingWhitespace);
                    firstNode.AddBeforeSelf(_entry.OriginalElement);
                }
                else
                {
                    firstNode.AddBeforeSelf(_entry.OriginalElement);
                }
            }
            else
            {
                if (_precedingWhitespace != null)
                {
                    root.Add(_precedingWhitespace);
                }
                root.Add(_entry.OriginalElement);
            }
        }

        _entries.Insert(_originalIndex, _entry);
        _document.HasUnsavedChanges = true;
    }
}
