using System.Globalization;
using TORTools.Core.Schema;

namespace TORTools.Core.Validation;

/// <summary>
/// Service for validating XML entry data against schema definitions.
/// </summary>
public class ValidationService : IValidationService
{
    /// <inheritdoc />
    public ValidationResult ValidateAll(IReadOnlyList<IDictionary<string, string>> entries, SchemaDefinition? schema, bool skipDuplicateIdCheck = false)
    {
        var result = new ValidationResult();

        // Collect all IDs for uniqueness validation (skip if equipment set variations)
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!skipDuplicateIdCheck)
        {
            foreach (var entry in entries)
            {
                if (entry.TryGetValue("id", out var id) && !string.IsNullOrEmpty(id))
                {
                    if (!allIds.Add(id))
                    {
                        duplicateIds.Add(id);
                    }
                }
            }
        }

        // Validate each entry
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var entryResult = ValidateEntry(entry, i, schema, allIds);
            result.Issues.AddRange(entryResult.Issues);

            // Check for duplicate IDs (skip if equipment set variations)
            if (!skipDuplicateIdCheck && entry.TryGetValue("id", out var id) && duplicateIds.Contains(id))
            {
                result.AddError(i, "id", $"Duplicate ID '{id}' - IDs must be unique", id, id);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateEntry(IDictionary<string, string> entry, int rowIndex, SchemaDefinition? schema, HashSet<string>? allIds = null)
    {
        var result = new ValidationResult();

        entry.TryGetValue("id", out var entryId);

        if (schema == null)
        {
            // No schema - skip validation
            return result;
        }

        // Validate each field defined in schema
        foreach (var (fieldName, fieldDef) in schema.Fields)
        {
            // Skip auto-filled and hidden fields
            if (!string.IsNullOrEmpty(fieldDef.AutoFillFrom))
                continue;

            entry.TryGetValue(fieldName, out var value);

            // Case-insensitive fallback
            if (value == null)
            {
                var key = entry.Keys.FirstOrDefault(k =>
                    k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (key != null)
                    value = entry[key];
            }

            var fieldResult = ValidateField(value, fieldName, fieldDef, rowIndex, entryId);
            result.Issues.AddRange(fieldResult.Issues);
        }

        return result;
    }

    /// <inheritdoc />
    public ValidationResult ValidateField(string? value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId = null)
    {
        var result = new ValidationResult();
        var isEmpty = IsEmpty(value);

        // Required field check
        if (fieldDef.Required && isEmpty)
        {
            result.AddError(rowIndex, fieldName, $"Required field '{fieldDef.DisplayName ?? fieldName}' is empty", entryId, value);
            return result; // Skip further validation if required field is empty
        }

        // Skip other validations if value is empty
        if (isEmpty)
            return result;

        // Type-specific validation
        switch (fieldDef.Type.ToLowerInvariant())
        {
            case "enum":
                ValidateEnum(value!, fieldName, fieldDef, rowIndex, entryId, result);
                break;
            // NOTE: int/float/bool validation disabled for now - can be noisy with existing data
            // case "int":
            //     ValidateInt(value!, fieldName, fieldDef, rowIndex, entryId, result);
            //     break;
            // case "float":
            //     ValidateFloat(value!, fieldName, fieldDef, rowIndex, entryId, result);
            //     break;
            // case "bool":
            //     ValidateBool(value!, fieldName, fieldDef, rowIndex, entryId, result);
            //     break;
        }

        return result;
    }

    private void ValidateInt(string value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId, ValidationResult result)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            result.AddError(rowIndex, fieldName, $"'{value}' is not a valid integer", entryId, value);
            return;
        }

        // Range validation
        if (fieldDef.Min.HasValue && intValue < fieldDef.Min.Value)
        {
            result.AddError(rowIndex, fieldName, $"Value {intValue} is below minimum {fieldDef.Min.Value}", entryId, value);
        }
        if (fieldDef.Max.HasValue && intValue > fieldDef.Max.Value)
        {
            result.AddError(rowIndex, fieldName, $"Value {intValue} is above maximum {fieldDef.Max.Value}", entryId, value);
        }
    }

    private void ValidateFloat(string value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId, ValidationResult result)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            result.AddError(rowIndex, fieldName, $"'{value}' is not a valid number", entryId, value);
            return;
        }

        // Range validation
        if (fieldDef.Min.HasValue && floatValue < fieldDef.Min.Value)
        {
            result.AddError(rowIndex, fieldName, $"Value {floatValue} is below minimum {fieldDef.Min.Value}", entryId, value);
        }
        if (fieldDef.Max.HasValue && floatValue > fieldDef.Max.Value)
        {
            result.AddError(rowIndex, fieldName, $"Value {floatValue} is above maximum {fieldDef.Max.Value}", entryId, value);
        }
    }

    private void ValidateBool(string value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId, ValidationResult result)
    {
        var normalized = value.ToLowerInvariant().Trim();
        if (normalized != "true" && normalized != "false")
        {
            result.AddError(rowIndex, fieldName, $"'{value}' is not a valid boolean (use 'true' or 'false')", entryId, value);
        }
    }

    private void ValidateEnum(string value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId, ValidationResult result)
    {
        if (fieldDef.EnumValues == null || fieldDef.EnumValues.Count == 0)
            return;

        var validValues = fieldDef.EnumValues.Select(e => e.Value).ToList();
        if (!validValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            var validList = string.Join(", ", validValues.Take(5));
            if (validValues.Count > 5)
                validList += $", ... ({validValues.Count - 5} more)";

            result.AddError(rowIndex, fieldName, $"'{value}' is not a valid value. Valid: {validList}", entryId, value);
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

    /// <inheritdoc />
    public ValidationResult ValidateCrossReferences(IReadOnlyList<IDictionary<string, string>> entries, string fieldName, ISet<string> availableIds)
    {
        var result = new ValidationResult();

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.TryGetValue("id", out var entryId);

            if (!entry.TryGetValue(fieldName, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            // Split by comma to get individual IDs
            var ids = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var id in ids)
            {
                var trimmedId = id.Trim();
                if (string.IsNullOrEmpty(trimmedId))
                    continue;

                if (!availableIds.Contains(trimmedId))
                {
                    result.AddError(i, fieldName, $"Invalid trait '{trimmedId}' does not exist", entryId, trimmedId);
                }
            }
        }

        return result;
    }
}
