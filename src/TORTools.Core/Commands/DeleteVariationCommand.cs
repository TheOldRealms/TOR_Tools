using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that deletes a variation (EquipmentSet) from a roster.
/// Unlike DeleteRowCommand which deletes the entire roster, this only removes
/// the specific variation element within the roster.
/// </summary>
public class DeleteVariationCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly XmlEntry _rosterEntry;
    private readonly XmlEntry _variationEntry;
    private XNode? _precedingWhitespace;
    private XNode? _insertAfter;

    public string Description => $"Delete Variation from {_rosterEntry.Id ?? "Roster"}";

    public DeleteVariationCommand(
        XmlDocumentWrapper document,
        XmlEntry rosterEntry,
        XmlEntry variationEntry)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _rosterEntry = rosterEntry ?? throw new ArgumentNullException(nameof(rosterEntry));
        _variationEntry = variationEntry ?? throw new ArgumentNullException(nameof(variationEntry));
    }

    public void Execute()
    {
        // Store the preceding whitespace for undo
        var prevNode = _variationEntry.OriginalElement.PreviousNode;
        if (prevNode is XText whitespace)
        {
            _precedingWhitespace = whitespace;
        }

        // Store what element to insert after when undoing
        _insertAfter = _precedingWhitespace?.PreviousNode ?? _variationEntry.OriginalElement.PreviousNode;

        // Remove whitespace and element
        _precedingWhitespace?.Remove();
        _variationEntry.OriginalElement.Remove();

        // Update the roster entry's children list
        _rosterEntry.RefreshChildren();

        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        var rosterElement = _rosterEntry.OriginalElement;

        // Check if our stored insertion point is still valid (has a parent in the document)
        var insertAfterValid = _insertAfter?.Parent != null;

        // Re-insert at original position if the insertion point is still valid
        if (_insertAfter != null && insertAfterValid)
        {
            if (_precedingWhitespace != null)
            {
                _insertAfter.AddAfterSelf(_precedingWhitespace);
                _precedingWhitespace.AddAfterSelf(_variationEntry.OriginalElement);
            }
            else
            {
                _insertAfter.AddAfterSelf(_variationEntry.OriginalElement);
            }
        }
        else
        {
            // Fallback: insert at end of roster element
            // This handles cases where the original insertion point was removed
            if (_precedingWhitespace != null)
            {
                rosterElement.Add(_precedingWhitespace);
            }
            rosterElement.Add(_variationEntry.OriginalElement);
        }

        // Update the roster entry's children list
        _rosterEntry.RefreshChildren();

        _document.HasUnsavedChanges = true;
    }
}