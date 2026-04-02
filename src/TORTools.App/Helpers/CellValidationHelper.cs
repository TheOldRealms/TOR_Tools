using TORTools.Core.Schema;
using TORTools.Core.Validation;

namespace TORTools.App.Helpers;

/// <summary>
/// Static helper for cell-level validation.
/// Delegates to ValidationService for actual validation logic to ensure consistency.
/// </summary>
public static class CellValidationHelper
{
    private static readonly ValidationService _validationService = new();

    /// <summary>
    /// Validates a cell value and registers any errors/warnings with the ValidationManager.
    /// Uses the same ValidationService as the async file-load validation for consistency.
    /// </summary>
    /// <param name="manager">The validation manager to register issues with.</param>
    /// <param name="rowIndex">The row index (0-based).</param>
    /// <param name="fieldName">The field/attribute name.</param>
    /// <param name="value">The current value to validate.</param>
    /// <param name="fieldDef">The field definition from schema.</param>
    /// <param name="entryId">Optional entry ID for error context.</param>
    /// <param name="forceRevalidate">If true, clears existing errors first. Use for edits.</param>
    public static void ValidateAndRegister(
        ValidationManager manager,
        int rowIndex,
        string fieldName,
        string? value,
        FieldDefinition? fieldDef,
        string? entryId,
        bool forceRevalidate = false)
    {
        if (fieldDef == null) return;

        // Skip cross-reference fields - they have their own validation logic in RebuildLinks
        if (fieldDef.CrossReference != null) return;

        // Only revalidate if forced (e.g., after an edit)
        // Otherwise, async validation on file load has already validated this cell
        if (forceRevalidate)
        {
            // Clear existing errors for this cell before re-validating
            manager.UnregisterErrors(rowIndex, fieldName);

            // Use ValidationService for actual validation (same as async validation)
            var result = _validationService.ValidateField(value, fieldName, fieldDef, rowIndex, entryId);

            // Register any issues found
            foreach (var issue in result.Issues)
            {
                var key = $"cell_{rowIndex}_{fieldName}_{issue.CurrentValue ?? "empty"}";
                manager.RegisterError(key, issue);
            }
        }
        // If not forced, validation was already done by async RunValidationAsync on file load
    }
}
