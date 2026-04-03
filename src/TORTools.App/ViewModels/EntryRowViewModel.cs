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
    private readonly Dictionary<string, string> _originalValues = new();
    private readonly Dictionary<string, string> _gitCommittedValues = new();
    private readonly HashSet<string> _modifiedFields = new();
    private readonly HashSet<string> _savedFields = new();
    private bool _isNew;
    private bool _wasNew;
    private bool _isRemoved;
    private bool _isSelectedForCopy;
    private bool _isIdLocked = true;
    private int _rowNumber;

    public XmlEntry XmlEntry { get; }

    /// <summary>
    /// For equipment set variations: the EquipmentSet child element.
    /// </summary>
    public XmlEntry? VariationEntry { get; set; }

    /// <summary>
    /// For equipment set variations: the index of this variation within the roster.
    /// </summary>
    public int VariationIndex { get; set; } = -1;

    /// <summary>
    /// Whether this row represents an equipment set variation (has nested structure).
    /// </summary>
    public bool IsEquipmentSetVariation => VariationEntry != null;

    /// <summary>
    /// Whether this is the first variation of a roster (variation index 0).
    /// Used for visual grouping - only first variation shows the roster ID.
    /// </summary>
    public bool IsFirstVariation => VariationIndex == 0;

    /// <summary>
    /// The roster ID this variation belongs to. Used for grouping.
    /// </summary>
    public string? RosterId { get; set; }

    /// <summary>
    /// Set of field names that have been modified from their original values.
    /// </summary>
    public IReadOnlySet<string> ModifiedFields => _modifiedFields;

    /// <summary>
    /// Checks if a specific field has been modified.
    /// </summary>
    public bool IsFieldModified(string fieldName) => _modifiedFields.Contains(fieldName);

    /// <summary>
    /// Set of field names that have been saved but not committed.
    /// </summary>
    public IReadOnlySet<string> SavedFields => _savedFields;

    /// <summary>
    /// Checks if a specific field has been saved but not committed.
    /// Only returns true if the current value is different from git committed value.
    /// </summary>
    public bool IsFieldSaved(string fieldName)
    {
        if (!_savedFields.Contains(fieldName))
            return false;

        // Only show as "saved" if value is still different from git committed value
        var currentValue = _values.TryGetValue(fieldName, out var val) ? val : "";
        var gitValue = _gitCommittedValues.TryGetValue(fieldName, out var git) ? git : "";
        return currentValue != gitValue;
    }

    /// <summary>
    /// Checks if a specific field differs from the git committed value.
    /// </summary>
    public bool IsFieldChangedFromGit(string fieldName)
    {
        var currentValue = _values.TryGetValue(fieldName, out var val) ? val : "";
        var gitValue = _gitCommittedValues.TryGetValue(fieldName, out var git) ? git : "";
        return currentValue != gitValue;
    }

    /// <summary>
    /// Marks all modified fields as saved (after file save).
    /// Moves fields from modified to saved state.
    /// Also marks WasNew if this was a new entry.
    /// </summary>
    public void MarkFieldsAsSaved()
    {
        // Track if this was a new entry before save
        if (_isNew)
        {
            WasNew = true;
        }

        foreach (var field in _modifiedFields)
        {
            _savedFields.Add(field);
        }
        _modifiedFields.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedFields)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SavedFields)));
    }

    /// <summary>
    /// Clears the saved fields tracking (e.g., after git commit or reload).
    /// </summary>
    public void ClearSavedFields()
    {
        _savedFields.Clear();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SavedFields)));
    }

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
    /// Whether this entry was newly created and has been saved (but not committed to git).
    /// Used for green text styling after save.
    /// </summary>
    public bool WasNew
    {
        get => _wasNew;
        set
        {
            if (_wasNew != value)
            {
                _wasNew = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WasNew)));
            }
        }
    }

    /// <summary>
    /// Whether this entry has been removed during this session.
    /// Removed entries are shown with strikethrough styling and are read-only.
    /// </summary>
    public bool IsRemoved
    {
        get => _isRemoved;
        set
        {
            if (_isRemoved != value)
            {
                _isRemoved = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRemoved)));
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

    public EntryRowViewModel(XmlEntry entry, IEnumerable<string> columnNames, Dictionary<string, string>? gitCommittedValues = null)
    {
        XmlEntry = entry;

        // Store git committed values if provided
        if (gitCommittedValues != null)
        {
            foreach (var kvp in gitCommittedValues)
            {
                _gitCommittedValues[kvp.Key] = kvp.Value;
            }
        }

        foreach (var col in columnNames)
        {
            var attr = entry.GetAttribute(col);
            var value = attr?.DisplayValue ?? "";
            _values[col] = value;
            _originalValues[col] = value;

            // If no git value was provided for this field, use the original value
            if (!_gitCommittedValues.ContainsKey(col))
            {
                _gitCommittedValues[col] = value;
            }
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

                // Track modification status
                var originalValue = _originalValues.TryGetValue(columnName, out var orig) ? orig : "";
                if (value != originalValue)
                {
                    _modifiedFields.Add(columnName);
                }
                else
                {
                    _modifiedFields.Remove(columnName);
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{columnName}]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedFields)));
                CellValueChanged?.Invoke(this, new CellValueChangedEventArgs(columnName, oldValue, value));
            }
        }
    }

    /// <summary>
    /// Sets a value without triggering change notifications or events.
    /// Used during initial population of equipment set rows.
    /// </summary>
    public void SetValueWithoutNotify(string columnName, string value)
    {
        _values[columnName] = value;
        _originalValues[columnName] = value; // Also set as original so it's not marked modified
    }

    /// <summary>
    /// Sets the original value for a field (used for cross-reference fields loaded after construction).
    /// </summary>
    public void SetOriginalValue(string columnName, string value)
    {
        _originalValues[columnName] = value;
        _values[columnName] = value;
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
