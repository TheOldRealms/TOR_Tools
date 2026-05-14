using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TORTools.Core.Models;

namespace TORTools.Mcp.Host.Services;

/// <summary>
/// Service for parsing and applying query filters to XML entries.
/// </summary>
public class QueryService
{
    /// <summary>
    /// Parses filter JSON into filter conditions.
    /// </summary>
    public List<FilterCondition> ParseFilters(string? filtersJson)
    {
        if (string.IsNullOrWhiteSpace(filtersJson))
            return new List<FilterCondition>();

        try
        {
            var filters = JsonSerializer.Deserialize<List<FilterCondition>>(filtersJson);
            return filters ?? new List<FilterCondition>();
        }
        catch (JsonException)
        {
            return new List<FilterCondition>();
        }
    }

    /// <summary>
    /// Applies filters to a collection of entries.
    /// </summary>
    public IEnumerable<XmlEntry> ApplyFilters(IEnumerable<XmlEntry> entries, List<FilterCondition> filters)
    {
        if (filters.Count == 0)
            return entries;

        return entries.Where(entry => MatchesAllFilters(entry, filters));
    }

    private bool MatchesAllFilters(XmlEntry entry, List<FilterCondition> filters)
    {
        // All filters must match (AND logic)
        return filters.All(filter => MatchesFilter(entry, filter));
    }

    private bool MatchesFilter(XmlEntry entry, FilterCondition filter)
    {
        var value = GetFieldValue(entry, filter.Field);
        var targetValue = filter.Value ?? "";

        return filter.Op.ToLowerInvariant() switch
        {
            "eq" or "=" or "==" => string.Equals(value, targetValue, StringComparison.OrdinalIgnoreCase),
            "neq" or "!=" or "<>" => !string.Equals(value, targetValue, StringComparison.OrdinalIgnoreCase),
            "gt" or ">" => CompareNumeric(value, targetValue) > 0,
            "gte" or ">=" => CompareNumeric(value, targetValue) >= 0,
            "lt" or "<" => CompareNumeric(value, targetValue) < 0,
            "lte" or "<=" => CompareNumeric(value, targetValue) <= 0,
            "contains" => value?.Contains(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "startswith" => value?.StartsWith(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "endswith" => value?.EndsWith(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "regex" => MatchesRegex(value, targetValue),
            "isnull" or "isempty" => string.IsNullOrWhiteSpace(value),
            "notnull" or "notempty" => !string.IsNullOrWhiteSpace(value),
            _ => false
        };
    }

    private static string? GetFieldValue(XmlEntry entry, string field)
    {
        // Check attributes first
        var attr = entry.GetAttribute(field);
        if (attr != null)
            return attr.DisplayValue;

        // Special fields
        return field.ToLowerInvariant() switch
        {
            "id" => entry.Id,
            "name" => entry.Name,
            "element" or "elementname" => entry.ElementName,
            _ => null
        };
    }

    private static int CompareNumeric(string? value1, string value2)
    {
        if (double.TryParse(value1, out var num1) && double.TryParse(value2, out var num2))
        {
            return num1.CompareTo(num2);
        }
        // Fall back to string comparison
        return string.Compare(value1, value2, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRegex(string? value, string pattern)
    {
        if (value == null)
            return false;

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }
        catch (RegexParseException)
        {
            return false;
        }
    }
}

/// <summary>
/// A filter condition for querying entries.
/// </summary>
public class FilterCondition
{
    /// <summary>
    /// The field/attribute name to filter on.
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    /// <summary>
    /// The operator: eq, neq, gt, gte, lt, lte, contains, startsWith, endsWith, regex, isnull, notnull
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "eq";

    /// <summary>
    /// The value to compare against.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
