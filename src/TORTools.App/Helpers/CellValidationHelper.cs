using System.Globalization;
using System.Text.RegularExpressions;
using TORTools.Core.Schema;
using TORTools.Core.Validation;

namespace TORTools.App.Helpers;

/// <summary>
/// Static helper for cell-level validation.
/// Cells call this to validate their values and register errors/warnings with the ValidationManager.
/// </summary>
public static class CellValidationHelper
{
    /// <summary>
    /// Validates a cell value and registers any errors/warnings with the ValidationManager.
    /// </summary>
    /// <param name="manager">The validation manager to register issues with.</param>
    /// <param name="rowIndex">The row index (0-based).</param>
    /// <param name="fieldName">The field/attribute name.</param>
    /// <param name="value">The current value to validate.</param>
    /// <param name="fieldDef">The field definition from schema.</param>
    /// <param name="entryId">Optional entry ID for error context.</param>
    public static void ValidateAndRegister(
        ValidationManager manager,
        int rowIndex,
        string fieldName,
        string? value,
        FieldDefinition? fieldDef,
        string? entryId)
    {
        // Unregister previous errors for this cell
        manager.UnregisterErrors(rowIndex, fieldName);

        if (fieldDef == null) return;

        // Skip cross-reference fields - they have their own validation logic
        if (fieldDef.CrossReference != null) return;

        var isEmpty = IsEmpty(value);

        // Required check (ERROR)
        if (fieldDef.Required && isEmpty)
        {
            manager.RegisterError(rowIndex, fieldName,
                $"Required field is empty", entryId, value);
            return; // Don't do further validation on empty required fields
        }

        if (isEmpty) return;

        // Enum check (ERROR)
        if (fieldDef.Type == "enum" && fieldDef.EnumValues?.Count > 0)
        {
            var validValues = fieldDef.EnumValues.Select(e => e.Value).ToList();
            if (!validValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                var validList = string.Join(", ", validValues.Take(5));
                if (validValues.Count > 5)
                    validList += $", ... ({validValues.Count - 5} more)";

                manager.RegisterError(rowIndex, fieldName,
                    $"'{value}' is not a valid option. Valid: {validList}", entryId, value);
            }
        }

        // Int check (ERROR)
        if (fieldDef.Type == "int")
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
            {
                manager.RegisterError(rowIndex, fieldName,
                    $"'{value}' is not a valid integer", entryId, value);
            }
            else
            {
                if (fieldDef.Min.HasValue && intVal < fieldDef.Min.Value)
                    manager.RegisterError(rowIndex, fieldName,
                        $"Value {intVal} is below minimum {fieldDef.Min.Value}", entryId, value);
                if (fieldDef.Max.HasValue && intVal > fieldDef.Max.Value)
                    manager.RegisterError(rowIndex, fieldName,
                        $"Value {intVal} is above maximum {fieldDef.Max.Value}", entryId, value);
            }
        }

        // Float check (ERROR)
        if (fieldDef.Type == "float")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
            {
                manager.RegisterError(rowIndex, fieldName,
                    $"'{value}' is not a valid number", entryId, value);
            }
            else
            {
                if (fieldDef.Min.HasValue && floatVal < fieldDef.Min.Value)
                    manager.RegisterError(rowIndex, fieldName,
                        $"Value {floatVal} is below minimum {fieldDef.Min.Value}", entryId, value);
                if (fieldDef.Max.HasValue && floatVal > fieldDef.Max.Value)
                    manager.RegisterError(rowIndex, fieldName,
                        $"Value {floatVal} is above maximum {fieldDef.Max.Value}", entryId, value);
            }
        }

        // Pattern check (WARNING)
        if (!string.IsNullOrEmpty(fieldDef.Pattern))
        {
            try
            {
                var regex = new Regex(fieldDef.Pattern);
                if (!regex.IsMatch(value!))
                {
                    manager.RegisterWarning(rowIndex, fieldName,
                        fieldDef.PatternWarning ?? $"Value doesn't match expected pattern '{fieldDef.Pattern}'",
                        entryId, value);
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern in schema - ignore
            }
        }
    }

    /// <summary>
    /// Check if a value is considered empty (null, whitespace, "-", "none").
    /// </summary>
    private static bool IsEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized == "-" || normalized == "none";
    }
}
