using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;
using TORTools.Mcp.Host.Services;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for querying and searching entries.
/// </summary>
[McpServerToolType]
public class QueryTools(IDocumentStore documentStore, QueryService queryService)
{
    [McpServerTool, Description("Query entries from an XML file with optional filters and sorting. Supports operators: eq, neq, gt, gte, lt, lte, contains, startsWith, endsWith, regex, in, between.")]
    public QueryEntriesResult query_entries(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Optional filters as JSON array (e.g., [{\"field\": \"culture\", \"op\": \"eq\", \"value\": \"empire\"}])")]
        string? filters = null,
        [Description("Sort by field (e.g., 'value desc', 'name asc', 'weight desc'). Default: no sorting.")]
        string? order_by = null,
        [Description("Maximum number of entries to return (default 50)")]
        int limit = 50,
        [Description("Number of entries to skip (for pagination)")]
        int offset = 0,
        [Description("Comma-separated list of fields to include in results (default: id, name). Use '*' for all fields.")]
        string? fields = null)
    {
        var allEntries = documentStore.GetEntries(file);
        if (allEntries.Count == 0)
        {
            return new QueryEntriesResult
            {
                Success = false,
                Error = $"File '{file}' not found or contains no entries."
            };
        }

        // Parse and apply filters
        var filterConditions = queryService.ParseFilters(filters);
        var filteredEntries = queryService.ApplyFilters(allEntries, filterConditions).ToList();

        // Apply sorting
        if (!string.IsNullOrWhiteSpace(order_by))
        {
            filteredEntries = ApplySorting(filteredEntries, order_by);
        }

        // Determine which fields to return
        var fieldsToInclude = ParseFields(fields);

        // Apply pagination
        var pagedEntries = filteredEntries
            .Skip(offset)
            .Take(limit)
            .Select(e => MapEntryWithFields(e, fieldsToInclude))
            .ToList();

        return new QueryEntriesResult
        {
            Success = true,
            TotalCount = filteredEntries.Count,
            ReturnedCount = pagedEntries.Count,
            Offset = offset,
            Entries = pagedEntries
        };
    }

    [McpServerTool, Description("Describe a file's structure: list all fields with their types, ranges, and sample values.")]
    public DescribeFileResult describe_file(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file)
    {
        var entries = documentStore.GetEntries(file);
        if (entries.Count == 0)
        {
            return new DescribeFileResult
            {
                Success = false,
                Error = $"File '{file}' not found or contains no entries."
            };
        }

        // Collect all field names and their values
        var fieldData = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        string? elementName = null;

        foreach (var entry in entries)
        {
            elementName ??= entry.ElementName;
            foreach (var attr in entry.Attributes)
            {
                if (!fieldData.ContainsKey(attr.Name))
                    fieldData[attr.Name] = new List<string?>();
                fieldData[attr.Name].Add(attr.RawValue);
            }
        }

        // Analyze each field
        var fieldDescriptions = new List<FieldDescription>();
        foreach (var (fieldName, values) in fieldData.OrderBy(f => f.Key))
        {
            var nonNullValues = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
            var distinctValues = nonNullValues.Distinct().ToList();

            var desc = new FieldDescription { Name = fieldName };

            // Try to determine if numeric
            var numericValues = nonNullValues
                .Select(v => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (double?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (numericValues.Count > nonNullValues.Count * 0.8) // 80% are numeric
            {
                desc.Type = "numeric";
                desc.Min = numericValues.Min().ToString(CultureInfo.InvariantCulture);
                desc.Max = numericValues.Max().ToString(CultureInfo.InvariantCulture);
                desc.SampleValues = distinctValues.Take(3).Where(v => v != null).Cast<string>().ToList();
            }
            else if (distinctValues.Count <= 20 && distinctValues.Count < nonNullValues.Count * 0.5)
            {
                // Looks like an enum (limited distinct values)
                desc.Type = "enum";
                desc.Values = distinctValues.Where(v => v != null).Cast<string>().OrderBy(v => v).ToList();
            }
            else
            {
                desc.Type = "string";
                desc.SampleValues = distinctValues.Take(5).Where(v => v != null).Cast<string>().ToList();
            }

            desc.NullCount = values.Count - nonNullValues.Count;
            fieldDescriptions.Add(desc);
        }

        return new DescribeFileResult
        {
            Success = true,
            EntryCount = entries.Count,
            ElementName = elementName ?? "Unknown",
            Fields = fieldDescriptions
        };
    }

    [McpServerTool, Description("Get distinct values for a field. Useful for discovering valid enum values, cultures, types, etc.")]
    public DistinctValuesResult distinct_values(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Field name to get distinct values for (e.g., 'culture', 'Type')")]
        string field)
    {
        var entries = documentStore.GetEntries(file);
        if (entries.Count == 0)
        {
            return new DistinctValuesResult
            {
                Success = false,
                Error = $"File '{file}' not found or contains no entries."
            };
        }

        var values = entries
            .Select(e => e.GetAttributeValue(field))
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return new DistinctValuesResult
        {
            Success = true,
            Field = field,
            Count = values.Count,
            Values = values!
        };
    }

    [McpServerTool, Description("Aggregate numeric field values: COUNT, MIN, MAX, AVG, SUM. Optionally filter first.")]
    public AggregateResult aggregate(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Field to aggregate (e.g., 'value', 'weight')")]
        string field,
        [Description("Aggregation operation: count, min, max, avg, sum")]
        string op,
        [Description("Optional filters as JSON array to filter before aggregating")]
        string? filters = null)
    {
        var entries = documentStore.GetEntries(file);
        if (entries.Count == 0)
        {
            return new AggregateResult
            {
                Success = false,
                Error = $"File '{file}' not found or contains no entries."
            };
        }

        // Apply filters if provided
        if (!string.IsNullOrWhiteSpace(filters))
        {
            var filterConditions = queryService.ParseFilters(filters);
            entries = queryService.ApplyFilters(entries, filterConditions).ToList();
        }

        var values = entries
            .Select(e => e.GetAttributeValue(field))
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (double?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        if (values.Count == 0 && op.ToLower() != "count")
        {
            return new AggregateResult
            {
                Success = false,
                Error = $"No numeric values found for field '{field}'."
            };
        }

        double result;
        string? minId = null, maxId = null;

        switch (op.ToLower())
        {
            case "count":
                result = entries.Count;
                break;
            case "min":
                result = values.Min();
                // Find the entry with min value
                minId = entries.FirstOrDefault(e =>
                    double.TryParse(e.GetAttributeValue(field), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d == result)?.Id;
                break;
            case "max":
                result = values.Max();
                // Find the entry with max value
                maxId = entries.FirstOrDefault(e =>
                    double.TryParse(e.GetAttributeValue(field), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d == result)?.Id;
                break;
            case "avg":
                result = values.Average();
                break;
            case "sum":
                result = values.Sum();
                break;
            default:
                return new AggregateResult
                {
                    Success = false,
                    Error = $"Unknown operation '{op}'. Use: count, min, max, avg, sum."
                };
        }

        return new AggregateResult
        {
            Success = true,
            Field = field,
            Operation = op.ToLower(),
            Result = result,
            EntryCount = entries.Count,
            MinEntryId = minId,
            MaxEntryId = maxId
        };
    }

    [McpServerTool, Description("Full-text search across all string fields in one or more files. Optionally filter results.")]
    public SearchResult search(
        [Description("Search query (case-insensitive, searches id, name, and all string attributes)")]
        string query,
        [Description("Comma-separated list of files to search, or '*' for all files")]
        string files = "*",
        [Description("Optional filters as JSON array to narrow results (e.g., [{\"field\": \"culture\", \"op\": \"contains\", \"value\": \"empire\"}])")]
        string? filters = null,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        var filesToSearch = new List<string>();

        if (files == "*")
        {
            filesToSearch = documentStore.GetAvailableFiles().Select(f => f.FileName).ToList();
        }
        else
        {
            filesToSearch = files.Split(',').Select(f => f.Trim()).ToList();
        }

        // Parse filters if provided
        var filterConditions = queryService.ParseFilters(filters);

        var results = new List<SearchHit>();
        var queryLower = query.ToLowerInvariant();

        foreach (var file in filesToSearch)
        {
            var entries = documentStore.GetEntries(file);

            // Apply filters first
            if (filterConditions.Count > 0)
            {
                entries = queryService.ApplyFilters(entries, filterConditions).ToList();
            }

            foreach (var entry in entries)
            {
                var matchingFields = new List<string>();

                foreach (var attr in entry.Attributes)
                {
                    if (attr.RawValue?.ToLowerInvariant().Contains(queryLower) == true)
                    {
                        matchingFields.Add(attr.Name);
                    }
                }

                if (matchingFields.Count > 0)
                {
                    results.Add(new SearchHit
                    {
                        File = file,
                        EntryId = entry.Id ?? "",
                        EntryName = entry.Name,
                        MatchingFields = matchingFields
                    });
                }
            }
        }

        return new SearchResult
        {
            Success = true,
            Query = query,
            HitCount = results.Count,
            Hits = results.Take(limit).ToList()
        };
    }

    private static List<XmlEntry> ApplySorting(List<XmlEntry> entries, string orderBy)
    {
        var parts = orderBy.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts[0];
        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return entries.OrderBy(e =>
        {
            var value = e.GetAttributeValue(field);
            // Try numeric sort first
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
                return numVal;
            return double.MaxValue; // Non-numeric values go to end
        }, descending ? Comparer<double>.Create((a, b) => b.CompareTo(a)) : Comparer<double>.Default)
        .ThenBy(e => e.GetAttributeValue(field) ?? "", descending ? StringComparer.OrdinalIgnoreCase : StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    [McpServerTool, Description("Find all entries that reference a given ID across one or all XML files.")]
    public FindReferencesResult find_references(
        [Description("The ID to search for references to")]
        string id,
        [Description("Optional: specific file to search in (searches all files if omitted)")]
        string? file = null)
    {
        var references = documentStore.FindReferences(id, file);

        return new FindReferencesResult
        {
            TargetId = id,
            ReferenceCount = references.Count,
            References = references.Select(r => new ReferenceDto
            {
                File = r.File,
                EntryId = r.EntryId,
                Field = r.Field
            }).ToList()
        };
    }

    private static HashSet<string> ParseFields(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            // Default fields
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name" };
        }

        return fields.Split(',')
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static QueryEntryDto MapEntryWithFields(XmlEntry entry, HashSet<string> fieldsToInclude)
    {
        var attributes = new Dictionary<string, string?>();

        // Always include id if present
        if (entry.Id != null)
            attributes["id"] = entry.Id;

        // Always include name if present
        if (entry.Name != null)
            attributes["name"] = entry.Name;

        // Add requested fields
        foreach (var attr in entry.Attributes)
        {
            if (fieldsToInclude.Contains(attr.Name) || fieldsToInclude.Contains("*"))
            {
                attributes[attr.Name] = attr.DisplayValue;
            }
        }

        return new QueryEntryDto
        {
            Id = entry.Id,
            Attributes = attributes
        };
    }
}

// Result DTOs

public class QueryEntriesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("returned_count")]
    public int ReturnedCount { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("entries")]
    public List<QueryEntryDto>? Entries { get; set; }
}

public class QueryEntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string?> Attributes { get; set; } = new();
}

public class FindReferencesResult
{
    [JsonPropertyName("target_id")]
    public string TargetId { get; set; } = "";

    [JsonPropertyName("reference_count")]
    public int ReferenceCount { get; set; }

    [JsonPropertyName("references")]
    public List<ReferenceDto> References { get; set; } = new();
}

public class ReferenceDto
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";
}

public class DescribeFileResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("entry_count")]
    public int EntryCount { get; set; }

    [JsonPropertyName("element_name")]
    public string ElementName { get; set; } = "";

    [JsonPropertyName("fields")]
    public List<FieldDescription> Fields { get; set; } = new();
}

public class FieldDescription
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string"; // "string", "numeric", "enum"

    [JsonPropertyName("min")]
    public string? Min { get; set; }

    [JsonPropertyName("max")]
    public string? Max { get; set; }

    [JsonPropertyName("values")]
    public List<string>? Values { get; set; } // For enum types

    [JsonPropertyName("sample_values")]
    public List<string>? SampleValues { get; set; }

    [JsonPropertyName("null_count")]
    public int NullCount { get; set; }
}

public class DistinctValuesResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = new();
}

public class AggregateResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "";

    [JsonPropertyName("result")]
    public double Result { get; set; }

    [JsonPropertyName("entry_count")]
    public int EntryCount { get; set; }

    [JsonPropertyName("min_entry_id")]
    public string? MinEntryId { get; set; }

    [JsonPropertyName("max_entry_id")]
    public string? MaxEntryId { get; set; }
}

public class SearchResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("hit_count")]
    public int HitCount { get; set; }

    [JsonPropertyName("hits")]
    public List<SearchHit> Hits { get; set; } = new();
}

public class SearchHit
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("entry_id")]
    public string EntryId { get; set; } = "";

    [JsonPropertyName("entry_name")]
    public string? EntryName { get; set; }

    [JsonPropertyName("matching_fields")]
    public List<string> MatchingFields { get; set; } = new();
}
