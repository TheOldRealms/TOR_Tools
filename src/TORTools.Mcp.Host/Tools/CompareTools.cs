using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for comparing and validating entries.
/// </summary>
[McpServerToolType]
public class CompareTools(IDocumentStore documentStore)
{
    [McpServerTool, Description("Compare attributes between multiple entries side-by-side. Useful for balancing items or checking consistency.")]
    public CompareEntriesResult compare_entries(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Comma-separated list of entry IDs to compare (e.g., 'tor_armor_1,tor_armor_2,tor_armor_3')")]
        string ids,
        [Description("Optional: Comma-separated list of fields to compare (compares all if omitted)")]
        string? fields = null)
    {
        var idList = ids.Split(',').Select(id => id.Trim()).Where(id => !string.IsNullOrEmpty(id)).ToList();

        if (idList.Count < 2)
        {
            return new CompareEntriesResult
            {
                Success = false,
                Error = "At least 2 entry IDs are required for comparison."
            };
        }

        var entries = new List<XmlEntry>();
        var notFound = new List<string>();

        foreach (var id in idList)
        {
            var entry = documentStore.GetEntry(file, id);
            if (entry != null)
                entries.Add(entry);
            else
                notFound.Add(id);
        }

        if (entries.Count < 2)
        {
            return new CompareEntriesResult
            {
                Success = false,
                Error = $"Need at least 2 entries to compare. Not found: {string.Join(", ", notFound)}"
            };
        }

        // Determine fields to compare
        var fieldsToCompare = ParseFields(fields, entries);

        // Build comparison table
        var comparison = new List<FieldComparisonRow>();
        foreach (var field in fieldsToCompare.OrderBy(f => f))
        {
            var values = entries.Select(e => GetFieldValue(e, field)).ToList();
            var allSame = values.Distinct().Count() <= 1;

            comparison.Add(new FieldComparisonRow
            {
                Field = field,
                Values = values,
                AllSame = allSame
            });
        }

        return new CompareEntriesResult
        {
            Success = true,
            EntryIds = entries.Select(e => e.Id ?? "").ToList(),
            NotFound = notFound.Count > 0 ? notFound : null,
            Comparison = comparison
        };
    }

    [McpServerTool, Description("Validate entries against their schema. Returns validation errors and warnings.")]
    public ValidateResult validate(
        [Description("File name to validate (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Optional: specific entry ID to validate (validates all entries if omitted)")]
        string? id = null)
    {
        var schema = documentStore.GetSchema(file);
        if (schema == null)
        {
            return new ValidateResult
            {
                Success = false,
                Error = $"No schema found for '{file}'. Cannot validate without a schema."
            };
        }

        var entries = string.IsNullOrEmpty(id)
            ? documentStore.GetEntries(file)
            : new[] { documentStore.GetEntry(file, id) }.Where(e => e != null).Cast<XmlEntry>().ToList();

        if (entries.Count == 0)
        {
            return new ValidateResult
            {
                Success = false,
                Error = id != null ? $"Entry '{id}' not found in '{file}'." : $"File '{file}' not found or empty."
            };
        }

        var issues = new List<ValidationIssue>();

        foreach (var entry in entries)
        {
            ValidateEntry(entry, schema, issues);
        }

        return new ValidateResult
        {
            Success = true,
            File = file,
            EntriesValidated = entries.Count,
            ErrorCount = issues.Count(i => i.Severity == "error"),
            WarningCount = issues.Count(i => i.Severity == "warning"),
            Issues = issues
        };
    }

    private void ValidateEntry(XmlEntry entry, Core.Schema.SchemaDefinition schema, List<ValidationIssue> issues)
    {
        var entryId = entry.Id ?? "(unknown)";

        foreach (var (fieldName, fieldDef) in schema.Fields)
        {
            var value = entry.GetAttributeValue(fieldName);

            // Required field check
            if (fieldDef.Required && string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new ValidationIssue
                {
                    EntryId = entryId,
                    Field = fieldName,
                    Severity = "error",
                    Message = $"Required field '{fieldName}' is missing or empty."
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Type validation
            switch (fieldDef.Type.ToLowerInvariant())
            {
                case "int":
                case "integer":
                    if (!int.TryParse(value, out var intVal))
                    {
                        issues.Add(new ValidationIssue
                        {
                            EntryId = entryId,
                            Field = fieldName,
                            Severity = "error",
                            Message = $"'{value}' is not a valid integer."
                        });
                    }
                    else
                    {
                        // Range check
                        if (fieldDef.Min.HasValue && intVal < fieldDef.Min.Value)
                        {
                            issues.Add(new ValidationIssue
                            {
                                EntryId = entryId,
                                Field = fieldName,
                                Severity = "warning",
                                Message = $"Value {intVal} is below minimum {fieldDef.Min.Value}."
                            });
                        }
                        if (fieldDef.Max.HasValue && intVal > fieldDef.Max.Value)
                        {
                            issues.Add(new ValidationIssue
                            {
                                EntryId = entryId,
                                Field = fieldName,
                                Severity = "warning",
                                Message = $"Value {intVal} exceeds maximum {fieldDef.Max.Value}."
                            });
                        }
                    }
                    break;

                case "float":
                case "number":
                    if (!double.TryParse(value, out var floatVal))
                    {
                        issues.Add(new ValidationIssue
                        {
                            EntryId = entryId,
                            Field = fieldName,
                            Severity = "error",
                            Message = $"'{value}' is not a valid number."
                        });
                    }
                    else
                    {
                        if (fieldDef.Min.HasValue && floatVal < fieldDef.Min.Value)
                        {
                            issues.Add(new ValidationIssue
                            {
                                EntryId = entryId,
                                Field = fieldName,
                                Severity = "warning",
                                Message = $"Value {floatVal} is below minimum {fieldDef.Min.Value}."
                            });
                        }
                        if (fieldDef.Max.HasValue && floatVal > fieldDef.Max.Value)
                        {
                            issues.Add(new ValidationIssue
                            {
                                EntryId = entryId,
                                Field = fieldName,
                                Severity = "warning",
                                Message = $"Value {floatVal} exceeds maximum {fieldDef.Max.Value}."
                            });
                        }
                    }
                    break;

                case "enum":
                    if (fieldDef.EnumValues != null && fieldDef.EnumValues.Count > 0)
                    {
                        var validValues = fieldDef.EnumValues.Select(e => e.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!validValues.Contains(value))
                        {
                            issues.Add(new ValidationIssue
                            {
                                EntryId = entryId,
                                Field = fieldName,
                                Severity = "error",
                                Message = $"'{value}' is not a valid enum value. Valid: {string.Join(", ", validValues.Take(5))}{(validValues.Count > 5 ? "..." : "")}"
                            });
                        }
                    }
                    break;

                case "bool":
                case "boolean":
                    if (!bool.TryParse(value, out _) && value != "0" && value != "1")
                    {
                        issues.Add(new ValidationIssue
                        {
                            EntryId = entryId,
                            Field = fieldName,
                            Severity = "error",
                            Message = $"'{value}' is not a valid boolean (expected true/false/0/1)."
                        });
                    }
                    break;
            }
        }

        // Check for duplicate IDs (done at file level, but flag here)
        // This would require passing in all entries, so skipping for now
    }

    private static HashSet<string> ParseFields(string? fields, List<XmlEntry> entries)
    {
        if (!string.IsNullOrWhiteSpace(fields))
        {
            return fields.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Collect all unique fields from all entries
        return entries
            .SelectMany(e => e.Attributes.Select(a => a.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetFieldValue(XmlEntry entry, string field)
    {
        var attr = entry.GetAttribute(field);
        return attr?.DisplayValue;
    }
}

// Result DTOs

public class CompareEntriesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("entry_ids")]
    public List<string>? EntryIds { get; set; }

    [JsonPropertyName("not_found")]
    public List<string>? NotFound { get; set; }

    [JsonPropertyName("comparison")]
    public List<FieldComparisonRow>? Comparison { get; set; }
}

public class FieldComparisonRow
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("values")]
    public List<string?> Values { get; set; } = new();

    [JsonPropertyName("all_same")]
    public bool AllSame { get; set; }
}

public class ValidateResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("entries_validated")]
    public int EntriesValidated { get; set; }

    [JsonPropertyName("error_count")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warning_count")]
    public int WarningCount { get; set; }

    [JsonPropertyName("issues")]
    public List<ValidationIssue>? Issues { get; set; }
}

public class ValidationIssue
{
    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
