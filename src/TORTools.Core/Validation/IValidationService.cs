using TORTools.Core.Schema;

namespace TORTools.Core.Validation;

/// <summary>
/// Service for validating XML entry data against schema definitions.
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// Validate all entries in a file against the schema.
    /// </summary>
    /// <param name="entries">The entries to validate (list of attribute dictionaries).</param>
    /// <param name="schema">The schema definition to validate against.</param>
    /// <param name="skipDuplicateIdCheck">If true, skip duplicate ID validation (e.g., for equipment set variations).</param>
    /// <returns>Validation result with all issues found.</returns>
    ValidationResult ValidateAll(IReadOnlyList<IDictionary<string, string>> entries, SchemaDefinition? schema, bool skipDuplicateIdCheck = false);

    /// <summary>
    /// Validate a single entry against the schema.
    /// </summary>
    /// <param name="entry">The entry to validate (attribute dictionary).</param>
    /// <param name="rowIndex">The row index for error reporting.</param>
    /// <param name="schema">The schema definition to validate against.</param>
    /// <param name="allIds">Optional set of all IDs for uniqueness validation.</param>
    /// <returns>Validation result with issues found in this entry.</returns>
    ValidationResult ValidateEntry(IDictionary<string, string> entry, int rowIndex, SchemaDefinition? schema, HashSet<string>? allIds = null);

    /// <summary>
    /// Validate a single field value against its field definition.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="fieldName">The field/attribute name.</param>
    /// <param name="fieldDef">The field definition from schema.</param>
    /// <param name="rowIndex">The row index for error reporting.</param>
    /// <param name="entryId">Optional entry ID for error reporting.</param>
    /// <returns>Validation result with issues found for this field.</returns>
    ValidationResult ValidateField(string? value, string fieldName, FieldDefinition fieldDef, int rowIndex, string? entryId = null);

    /// <summary>
    /// Validate cross-reference field values against available IDs.
    /// </summary>
    /// <param name="entries">The entries to validate.</param>
    /// <param name="fieldName">The cross-reference field name.</param>
    /// <param name="availableIds">Set of valid IDs.</param>
    /// <returns>Validation result with invalid reference issues.</returns>
    ValidationResult ValidateCrossReferences(IReadOnlyList<IDictionary<string, string>> entries, string fieldName, ISet<string> availableIds);
}
