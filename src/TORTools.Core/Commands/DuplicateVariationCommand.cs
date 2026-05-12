using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that duplicates a variation (EquipmentSet) within the same roster.
/// </summary>
public class DuplicateVariationCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly XmlEntry _rosterEntry;
    private readonly XmlEntry _variationEntry;
    private XmlEntry? _duplicatedVariation;
    private XText? _addedWhitespace;

    /// <summary>
    /// The newly created variation entry, available after Execute().
    /// </summary>
    public XmlEntry? DuplicatedVariation => _duplicatedVariation;

    public string Description => $"Duplicate Variation in {_rosterEntry.Id ?? "Roster"}";

    public DuplicateVariationCommand(
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
        var variationElement = _variationEntry.OriginalElement;

        // Clone the variation element with all its children (Equipment elements)
        var newVariation = new XElement(variationElement);

        // Determine indentation
        var variationIndent = GetIndentation(variationElement);
        var whitespaceText = "\n" + variationIndent;
        _addedWhitespace = new XText(whitespaceText);

        // Insert after the original variation
        variationElement.AddAfterSelf(_addedWhitespace);
        _addedWhitespace.AddAfterSelf(newVariation);

        _duplicatedVariation = new XmlEntry(newVariation);

        // Update the roster entry's children list
        _rosterEntry.RefreshChildren();

        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        if (_duplicatedVariation == null) return;

        _addedWhitespace?.Remove();
        _duplicatedVariation.OriginalElement.Remove();
        _duplicatedVariation = null;
        _addedWhitespace = null;

        // Update the roster entry's children list
        _rosterEntry.RefreshChildren();

        _document.HasUnsavedChanges = true;
    }

    private string GetIndentation(XElement element)
    {
        var previousText = element.PreviousNode as XText;
        if (previousText != null)
        {
            var text = previousText.Value;
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline >= 0)
            {
                return text.Substring(lastNewline + 1);
            }
        }
        return _document.IndentString + _document.IndentString;
    }
}
