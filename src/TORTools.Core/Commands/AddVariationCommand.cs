using System.Xml.Linq;
using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that adds a new variation (EquipmentSet) to an existing roster.
/// </summary>
public class AddVariationCommand : IEditCommand
{
    private readonly XmlDocumentWrapper _document;
    private readonly XmlEntry _rosterEntry;
    private readonly string _variationElementName;
    private XmlEntry? _addedVariation;
    private XText? _addedWhitespace;

    /// <summary>
    /// The newly created variation entry, available after Execute().
    /// </summary>
    public XmlEntry? AddedVariation => _addedVariation;

    public string Description => $"Add Variation to {_rosterEntry.Id ?? "Roster"}";

    public AddVariationCommand(
        XmlDocumentWrapper document,
        XmlEntry rosterEntry,
        string variationElementName = "EquipmentSet")
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _rosterEntry = rosterEntry ?? throw new ArgumentNullException(nameof(rosterEntry));
        _variationElementName = variationElementName;
    }

    public void Execute()
    {
        var rosterElement = _rosterEntry.OriginalElement;

        // Create a new empty variation element
        var newVariation = new XElement(_variationElementName);

        // Find the last variation element to insert after, or insert as first child
        var lastVariation = rosterElement.Elements(_variationElementName).LastOrDefault();

        // Determine indentation (roster indent + one level)
        var rosterIndent = GetIndentation(rosterElement);
        var variationIndent = rosterIndent + _document.IndentString;
        var whitespaceText = "\n" + variationIndent;
        _addedWhitespace = new XText(whitespaceText);

        if (lastVariation != null)
        {
            // Insert after the last variation
            lastVariation.AddAfterSelf(_addedWhitespace);
            _addedWhitespace.AddAfterSelf(newVariation);
        }
        else
        {
            // No variations yet - add as first child
            var firstChild = rosterElement.Nodes().FirstOrDefault();
            if (firstChild != null)
            {
                firstChild.AddBeforeSelf(_addedWhitespace);
                _addedWhitespace.AddAfterSelf(newVariation);
            }
            else
            {
                rosterElement.Add(_addedWhitespace);
                rosterElement.Add(newVariation);
                // Add closing whitespace
                rosterElement.Add(new XText("\n" + rosterIndent));
            }
        }

        _addedVariation = new XmlEntry(newVariation);

        // Update the roster entry's children list
        _rosterEntry.RefreshChildren();

        _document.HasUnsavedChanges = true;
    }

    public void Undo()
    {
        if (_addedVariation == null) return;

        _addedWhitespace?.Remove();
        _addedVariation.OriginalElement.Remove();
        _addedVariation = null;
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
        return _document.IndentString;
    }
}
