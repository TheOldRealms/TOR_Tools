using System.Collections.Generic;
using System.ComponentModel;
using TORTools.Core.Models;

namespace TORTools.App.ViewModels;

/// <summary>
/// Event args for when a cell value changes.
/// </summary>
public class CellValueChangedEventArgs : EventArgs
{
    public string ColumnName { get; }
    public string OldValue { get; }
    public string NewValue { get; }

    public CellValueChangedEventArgs(string columnName, string oldValue, string newValue)
    {
        ColumnName = columnName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// A row in the DataGrid that wraps an XmlEntry.
/// Uses an indexer to allow dynamic column access.
/// </summary>
public class EntryRowViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _values = new();
    private bool _isNew;
    private bool _isSelectedForCopy;
    private bool _isIdLocked = true;
    private int _rowNumber;

    public XmlEntry XmlEntry { get; }

    /// <summary>
    /// Whether this is a newly created entry (not yet saved).
    /// New entries have editable IDs and green styling.
    /// </summary>
    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew != value)
            {
                _isNew = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNew)));
            }
        }
    }

    /// <summary>
    /// Whether this row is selected as the source for copy/paste operations.
    /// </summary>
    public bool IsSelectedForCopy
    {
        get => _isSelectedForCopy;
        set
        {
            if (_isSelectedForCopy != value)
            {
                _isSelectedForCopy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForCopy)));
            }
        }
    }

    /// <summary>
    /// Whether the ID field is locked for editing.
    /// Default is true (locked) for existing entries, false for new entries.
    /// </summary>
    public bool IsIdLocked
    {
        get => _isIdLocked;
        set
        {
            if (_isIdLocked != value)
            {
                _isIdLocked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIdLocked)));
            }
        }
    }

    /// <summary>
    /// Row number for display (1-based).
    /// </summary>
    public int RowNumber
    {
        get => _rowNumber;
        set
        {
            if (_rowNumber != value)
            {
                _rowNumber = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowNumber)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<CellValueChangedEventArgs>? CellValueChanged;

    public EntryRowViewModel(XmlEntry entry, IEnumerable<string> columnNames)
    {
        XmlEntry = entry;

        foreach (var col in columnNames)
        {
            var attr = entry.GetAttribute(col);
            _values[col] = attr?.DisplayValue ?? "";
        }
    }

    /// <summary>
    /// Gets or sets a column value by name.
    /// </summary>
    public string this[string columnName]
    {
        get => _values.TryGetValue(columnName, out var val) ? val : "";
        set
        {
            // Block ID edits when locked
            if (columnName.Equals("id", StringComparison.OrdinalIgnoreCase) && IsIdLocked)
                return;

            var oldValue = _values.TryGetValue(columnName, out var val) ? val : "";
            if (oldValue != value)
            {
                _values[columnName] = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{columnName}]"));
                CellValueChanged?.Invoke(this, new CellValueChangedEventArgs(columnName, oldValue, value));
            }
        }
    }

    /// <summary>
    /// Sets a value without triggering CellValueChanged (used for undo/redo).
    /// </summary>
    public void SetValueSilent(string columnName, string value)
    {
        _values[columnName] = value;
        // Notify both the specific item and the general indexer to force DataGrid refresh
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{columnName}]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// Gets all column names.
    /// </summary>
    public IEnumerable<string> ColumnNames => _values.Keys;

    /// <summary>
    /// Forces property changed notifications for all values (used after bulk updates).
    /// </summary>
    public void NotifyAllValuesChanged()
    {
        foreach (var col in _values.Keys)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{col}]"));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
