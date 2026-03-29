using TORTools.Core.Models;

namespace TORTools.Core.Commands;

/// <summary>
/// Command that represents editing a single cell (attribute value) in an entry.
/// </summary>
public class EditCellCommand : IEditCommand
{
    private readonly XmlEntry _entry;
    private readonly string _attributeName;
    private readonly string _oldValue;
    private readonly string _newValue;

    public string Description => $"Edit {_attributeName}";

    public EditCellCommand(XmlEntry entry, string attributeName, string oldValue, string newValue)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _attributeName = attributeName ?? throw new ArgumentNullException(nameof(attributeName));
        _oldValue = oldValue ?? "";
        _newValue = newValue ?? "";
    }

    public void Execute()
    {
        _entry.SetAttributeValue(_attributeName, _newValue);
    }

    public void Undo()
    {
        _entry.SetAttributeValue(_attributeName, _oldValue);
    }
}
