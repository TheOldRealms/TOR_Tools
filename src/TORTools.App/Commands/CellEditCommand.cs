using TORTools.App.ViewModels;
using TORTools.Core.Commands;
using TORTools.Core.Models;
using TORTools.Core.Services;

namespace TORTools.App.Commands;

/// <summary>
/// Command for editing a cell value with undo/redo support.
/// Updates both the row view model and the underlying XML entry.
/// </summary>
public class CellEditCommand : IEditCommand
{
    private readonly EntryRowViewModel _rowVm;
    private readonly string _columnName;
    private readonly string _oldValue;
    private readonly string _newValue;
    private readonly string? _nestedPath;

    public string Description => $"Edit {_columnName}";

    public CellEditCommand(EntryRowViewModel rowVm, string columnName, string oldValue, string newValue, string? nestedPath = null)
    {
        _rowVm = rowVm;
        _columnName = columnName;
        _oldValue = oldValue;
        _newValue = newValue;
        _nestedPath = nestedPath;
    }

    public void Execute()
    {
        _rowVm.SetValueSilent(_columnName, _newValue);
        UpdateXmlEntry(_newValue);
    }

    public void Undo()
    {
        _rowVm.SetValueSilent(_columnName, _oldValue);
        UpdateXmlEntry(_oldValue);
    }

    private void UpdateXmlEntry(string value)
    {
        // Handle nested fields
        if (!string.IsNullOrEmpty(_nestedPath))
        {
            _rowVm.XmlEntry.SetNestedValue(_nestedPath, value);
            return;
        }

        var attr = _rowVm.XmlEntry.GetAttribute(_columnName);
        if (attr != null)
        {
            var rawValue = LocalizationHelper.Wrap(attr.LocalizationKey, value);
            _rowVm.XmlEntry.SetAttributeValue(_columnName, rawValue);
        }
        else
        {
            // New attribute - add it directly without localization wrapping
            _rowVm.XmlEntry.SetAttributeValue(_columnName, value);
        }
    }
}
