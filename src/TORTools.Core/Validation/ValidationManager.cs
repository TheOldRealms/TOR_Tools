namespace TORTools.Core.Validation;

/// <summary>
/// Central registry for validation issues. Cells/fields register their errors here,
/// and the validation panel displays whatever is registered.
/// </summary>
public class ValidationManager
{
    private readonly Dictionary<string, ValidationIssue> _issues = new();
    private readonly object _lock = new();

    /// <summary>
    /// Event raised when the issues collection changes.
    /// </summary>
    public event EventHandler? IssuesChanged;

    /// <summary>
    /// Gets all current validation issues.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues
    {
        get
        {
            lock (_lock)
            {
                return _issues.Values.ToList();
            }
        }
    }

    /// <summary>
    /// Gets the count of errors.
    /// </summary>
    public int ErrorCount
    {
        get
        {
            lock (_lock)
            {
                return _issues.Values.Count(i => i.Severity == ValidationSeverity.Error);
            }
        }
    }

    /// <summary>
    /// Gets the count of warnings.
    /// </summary>
    public int WarningCount
    {
        get
        {
            lock (_lock)
            {
                return _issues.Values.Count(i => i.Severity == ValidationSeverity.Warning);
            }
        }
    }

    /// <summary>
    /// Registers a validation error. If an error with the same key exists, it's replaced.
    /// </summary>
    /// <param name="key">Unique key for this error (e.g., "row_15_ItemTraits_fire_weapon")</param>
    /// <param name="issue">The validation issue</param>
    public void RegisterError(string key, ValidationIssue issue)
    {
        lock (_lock)
        {
            _issues[key] = issue;
        }
        IssuesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Registers a validation error with auto-generated key.
    /// </summary>
    public void RegisterError(int rowIndex, string fieldName, string message, string? entryId = null, string? value = null)
    {
        var key = $"row_{rowIndex}_{fieldName}_{value ?? "empty"}";
        var issue = new ValidationIssue
        {
            Severity = ValidationSeverity.Error,
            RowIndex = rowIndex,
            AttributeName = fieldName,
            Message = message,
            EntryId = entryId,
            CurrentValue = value
        };
        RegisterError(key, issue);
    }

    /// <summary>
    /// Registers a validation warning with auto-generated key.
    /// </summary>
    public void RegisterWarning(int rowIndex, string fieldName, string message, string? entryId = null, string? value = null)
    {
        var key = $"row_{rowIndex}_{fieldName}_{value ?? "empty"}";
        var issue = new ValidationIssue
        {
            Severity = ValidationSeverity.Warning,
            RowIndex = rowIndex,
            AttributeName = fieldName,
            Message = message,
            EntryId = entryId,
            CurrentValue = value
        };
        RegisterError(key, issue);
    }

    /// <summary>
    /// Unregisters a validation error by key.
    /// </summary>
    public void UnregisterError(string key)
    {
        bool removed;
        lock (_lock)
        {
            removed = _issues.Remove(key);
        }
        if (removed)
        {
            IssuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Unregisters all errors for a specific row and field.
    /// </summary>
    public void UnregisterErrors(int rowIndex, string fieldName)
    {
        var keysToRemove = new List<string>();
        lock (_lock)
        {
            foreach (var kvp in _issues)
            {
                if (kvp.Value.RowIndex == rowIndex && kvp.Value.AttributeName == fieldName)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _issues.Remove(key);
            }
        }
        if (keysToRemove.Count > 0)
        {
            IssuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Clears all validation issues.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _issues.Clear();
        }
        IssuesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears validation issues with keys that start with the specified prefix.
    /// </summary>
    /// <param name="prefix">The key prefix to match.</param>
    public void ClearByPrefix(string prefix)
    {
        var keysToRemove = new List<string>();
        lock (_lock)
        {
            foreach (var key in _issues.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _issues.Remove(key);
            }
        }
        if (keysToRemove.Count > 0)
        {
            IssuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
