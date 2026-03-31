namespace TORTools.Core.Validation;

/// <summary>
/// Severity level for validation issues.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational message, not an error.</summary>
    Info,
    /// <summary>Warning - data is valid but may cause issues.</summary>
    Warning,
    /// <summary>Error - data is invalid and should be fixed.</summary>
    Error
}

/// <summary>
/// Represents a single validation issue found in the data.
/// </summary>
public class ValidationIssue
{
    /// <summary>
    /// The row index (0-based) where the issue was found.
    /// </summary>
    public int RowIndex { get; init; }

    /// <summary>
    /// The attribute/column name where the issue was found.
    /// </summary>
    public string AttributeName { get; init; } = "";

    /// <summary>
    /// Human-readable description of the issue.
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Severity level of this issue.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// The ID of the affected entry (if available).
    /// </summary>
    public string? EntryId { get; init; }

    /// <summary>
    /// The current value that caused the issue.
    /// </summary>
    public string? CurrentValue { get; init; }

    public override string ToString()
    {
        var location = string.IsNullOrEmpty(EntryId) ? $"Row {RowIndex + 1}" : $"'{EntryId}'";
        return $"[{Severity}] {location}.{AttributeName}: {Message}";
    }
}

/// <summary>
/// Result of validating a file or set of entries.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// All validation issues found.
    /// </summary>
    public List<ValidationIssue> Issues { get; } = new();

    /// <summary>
    /// Whether any errors were found.
    /// </summary>
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Whether any warnings were found.
    /// </summary>
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Whether validation passed with no errors.
    /// </summary>
    public bool IsValid => !HasErrors;

    /// <summary>
    /// Count of errors.
    /// </summary>
    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Count of warnings.
    /// </summary>
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);

    /// <summary>
    /// Add an error issue.
    /// </summary>
    public void AddError(int rowIndex, string attributeName, string message, string? entryId = null, string? currentValue = null)
    {
        Issues.Add(new ValidationIssue
        {
            RowIndex = rowIndex,
            AttributeName = attributeName,
            Message = message,
            Severity = ValidationSeverity.Error,
            EntryId = entryId,
            CurrentValue = currentValue
        });
    }

    /// <summary>
    /// Add a warning issue.
    /// </summary>
    public void AddWarning(int rowIndex, string attributeName, string message, string? entryId = null, string? currentValue = null)
    {
        Issues.Add(new ValidationIssue
        {
            RowIndex = rowIndex,
            AttributeName = attributeName,
            Message = message,
            Severity = ValidationSeverity.Warning,
            EntryId = entryId,
            CurrentValue = currentValue
        });
    }
}
