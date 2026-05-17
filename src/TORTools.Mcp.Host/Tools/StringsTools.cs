using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;
using TORTools.Core.Services;

using static TORTools.Core.DocumentStore.StandaloneDocumentStore;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for working with localization strings (tor_strings.xml).
/// </summary>
[McpServerToolType]
public class StringsTools(IDocumentStore documentStore)
{
    private const string StringsFile = "tor_strings.xml";

    [McpServerTool, Description("Get a single localization string by its ID.")]
    public StringGetResult strings_get(
        [Description("String ID to retrieve (e.g., 'tor_skill_faith' or vanilla hash like 'GAsVO8cZ')")]
        string id)
    {
        Log("Tool", $"strings_get(id={id})");
        var entry = documentStore.GetEntry(StringsFile, id);
        if (entry == null)
        {
            return new StringGetResult
            {
                Found = false,
                Error = $"String '{id}' not found in {StringsFile}."
            };
        }

        return new StringGetResult
        {
            Found = true,
            String = MapStringEntry(entry)
        };
    }

    [McpServerTool, Description("Query localization strings with optional filters. Supports searching by ID pattern, text content, category, subcategory, or tags. Use this to find contextually related strings.")]
    public StringQueryResult strings_query(
        [Description("Filter by ID pattern (supports * wildcards, e.g., 'tor_skill_*' or '*_description')")]
        string? id_pattern = null,
        [Description("Filter by text content (case-insensitive contains)")]
        string? text_contains = null,
        [Description("Filter by category (exact match, e.g., 'Skill perks', 'Career Choice', 'UI')")]
        string? category = null,
        [Description("Filter by subcategory (exact match, e.g., 'Faith', 'Grail Knight', 'Vampire')")]
        string? subcategory = null,
        [Description("Filter by tag name (e.g., 'IsOrcTag', 'EmpireTag')")]
        string? has_tag = null,
        [Description("Include full text content in results (default false for browsing, set true when you need the actual text)")]
        bool include_text = false,
        [Description("Maximum number of strings to return (default 50)")]
        int limit = 50,
        [Description("Number of strings to skip (for pagination)")]
        int offset = 0)
    {
        Log("Tool", $"strings_query(id_pattern={id_pattern ?? "null"}, text_contains={text_contains ?? "null"}, category={category ?? "null"}, subcategory={subcategory ?? "null"}, has_tag={has_tag ?? "null"}, include_text={include_text}, limit={limit}, offset={offset})");

        var entries = documentStore.GetEntries(StringsFile);
        if (entries.Count == 0)
        {
            return new StringQueryResult
            {
                Success = false,
                Error = $"File '{StringsFile}' not found or contains no entries."
            };
        }

        // Apply filters
        IEnumerable<XmlEntry> filtered = entries;

        if (!string.IsNullOrWhiteSpace(id_pattern))
        {
            var pattern = id_pattern.Replace("*", ".*");
            var regex = new System.Text.RegularExpressions.Regex($"^{pattern}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            filtered = filtered.Where(e => e.Id != null && regex.IsMatch(e.Id));
        }

        if (!string.IsNullOrWhiteSpace(text_contains))
        {
            var search = text_contains.ToLowerInvariant();
            filtered = filtered.Where(e =>
            {
                var text = e.GetAttribute("text")?.DisplayValue;
                return text?.ToLowerInvariant().Contains(search) == true;
            });
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filtered = filtered.Where(e =>
            {
                var cat = e.GetAttributeValue("category");
                return string.Equals(cat, category, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (!string.IsNullOrWhiteSpace(subcategory))
        {
            filtered = filtered.Where(e =>
            {
                var subcat = e.GetAttributeValue("subcategory");
                return string.Equals(subcat, subcategory, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (!string.IsNullOrWhiteSpace(has_tag))
        {
            var tagSearch = has_tag.ToLowerInvariant();
            filtered = filtered.Where(e =>
            {
                var tags = e.GetTagList("tags", "tag", "tag_name", "weight");
                return tags?.ToLowerInvariant().Contains(tagSearch) == true;
            });
        }

        var filteredList = filtered.ToList();
        var pagedEntries = filteredList
            .Skip(offset)
            .Take(limit)
            .Select(e => MapStringEntry(e, include_text))
            .ToList();

        return new StringQueryResult
        {
            Success = true,
            TotalCount = filteredList.Count,
            ReturnedCount = pagedEntries.Count,
            Offset = offset,
            Strings = pagedEntries
        };
    }

    [McpServerTool, Description("Full-text search across all localization string text. Returns strings containing the search query.")]
    public StringSearchResult strings_search(
        [Description("Search query (case-insensitive, searches in both ID and text)")]
        string query,
        [Description("Maximum number of results to return (default 50)")]
        int limit = 50)
    {
        Log("Tool", $"strings_search(query={query}, limit={limit})");

        var entries = documentStore.GetEntries(StringsFile);
        if (entries.Count == 0)
        {
            return new StringSearchResult
            {
                Success = false,
                Error = $"File '{StringsFile}' not found or contains no entries."
            };
        }

        var queryLower = query.ToLowerInvariant();
        var matches = entries
            .Where(e =>
            {
                var id = e.Id?.ToLowerInvariant() ?? "";
                var text = e.GetAttribute("text")?.DisplayValue?.ToLowerInvariant() ?? "";
                return id.Contains(queryLower) || text.Contains(queryLower);
            })
            .Take(limit)
            .Select(e => MapStringEntry(e, true))
            .ToList();

        return new StringSearchResult
        {
            Success = true,
            Query = query,
            MatchCount = matches.Count,
            Strings = matches
        };
    }

    [McpServerTool, Description("Add a new localization string. Use strings_list_categories to see available categories. The string will be grouped with others in the same category when saved.")]
    public StringAddResult strings_add(
        [Description("Unique string ID (e.g., 'tor_myfeature_label')")]
        string id,
        [Description("Display text (without localization key - it will be auto-generated)")]
        string text,
        [Description("Category for grouping (e.g., 'Skill perks', 'Career Choice', 'UI'). Use strings_list_categories to see existing categories.")]
        string? category = null,
        [Description("Subcategory for finer grouping (e.g., 'Faith', 'Grail Knight')")]
        string? subcategory = null,
        [Description("Optional: Comma-separated tags for conditional selection (e.g., 'IsOrcTag, EmpireTag')")]
        string? tags = null)
    {
        Log("Tool", $"strings_add(id={id}, text={text}, category={category ?? "null"}, subcategory={subcategory ?? "null"}, tags={tags ?? "null"})");

        // Check if ID already exists
        var existing = documentStore.GetEntry(StringsFile, id);
        if (existing != null)
        {
            return new StringAddResult
            {
                Success = false,
                Error = $"String with ID '{id}' already exists."
            };
        }

        // If category specified, find a template from that category for better placement
        var entries = documentStore.GetEntries(StringsFile);
        string? templateId = null;

        if (!string.IsNullOrWhiteSpace(category))
        {
            // Find an existing entry in the same category (and subcategory if specified)
            var categoryMatch = entries.FirstOrDefault(e =>
            {
                var cat = e.GetAttributeValue("category");
                if (!string.Equals(cat, category, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(subcategory))
                {
                    var subcat = e.GetAttributeValue("subcategory");
                    return string.Equals(subcat, subcategory, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            });

            templateId = categoryMatch?.Id ?? entries.FirstOrDefault()?.Id;
        }
        else
        {
            templateId = entries.FirstOrDefault()?.Id;
        }

        var newEntry = documentStore.CreateEntry(StringsFile, templateId);
        if (newEntry == null)
        {
            return new StringAddResult
            {
                Success = false,
                Error = $"Failed to create new string entry."
            };
        }

        // Generate localization key from ID
        var locKey = id.StartsWith("str_") ? id : LocalizationHelper.GenerateKey(id);
        var wrappedText = LocalizationHelper.Wrap(locKey, text);

        // Update the entry with all attributes including category/subcategory
        var attrs = new Dictionary<string, string?>
        {
            ["id"] = id,
            ["text"] = wrappedText
        };

        // Set category and subcategory if provided (these go to metadata file)
        if (!string.IsNullOrWhiteSpace(category))
        {
            attrs["category"] = category;
        }
        if (!string.IsNullOrWhiteSpace(subcategory))
        {
            attrs["subcategory"] = subcategory;
        }

        documentStore.UpdateEntry(StringsFile, newEntry.Id!, attrs);

        // Set tags if provided
        if (!string.IsNullOrWhiteSpace(tags))
        {
            newEntry = documentStore.GetEntry(StringsFile, id);
            newEntry?.SetTagList(tags, "tags", "tag", "tag_name", "weight");
            documentStore.SaveDocument(StringsFile);
        }

        newEntry = documentStore.GetEntry(StringsFile, id);

        return new StringAddResult
        {
            Success = true,
            String = newEntry != null ? MapStringEntry(newEntry) : null
        };
    }

    [McpServerTool, Description("Update an existing localization string's text and/or tags.")]
    public StringUpdateResult strings_update(
        [Description("String ID to update")]
        string id,
        [Description("New display text (optional - set to update text)")]
        string? text = null,
        [Description("New tags as comma-separated string (optional - set to update tags, empty string to clear tags)")]
        string? tags = null)
    {
        Log("Tool", $"strings_update(id={id}, text={text ?? "null"}, tags={tags ?? "null"})");

        var entry = documentStore.GetEntry(StringsFile, id);
        if (entry == null)
        {
            return new StringUpdateResult
            {
                Success = false,
                Error = $"String '{id}' not found in {StringsFile}."
            };
        }

        var updated = new List<string>();

        if (text != null)
        {
            // Preserve or generate localization key
            var existingAttr = entry.GetAttribute("text");
            var locKey = existingAttr?.LocalizationKey ?? LocalizationHelper.GenerateKey(id);
            var wrappedText = LocalizationHelper.Wrap(locKey, text);

            var attrs = new Dictionary<string, string?> { ["text"] = wrappedText };
            documentStore.UpdateEntry(StringsFile, id, attrs);
            updated.Add("text");
        }

        if (tags != null)
        {
            // Reload entry after text update
            entry = documentStore.GetEntry(StringsFile, id);
            entry?.SetTagList(string.IsNullOrWhiteSpace(tags) ? null : tags, "tags", "tag", "tag_name", "weight");
            documentStore.SaveDocument(StringsFile);
            updated.Add("tags");
        }

        entry = documentStore.GetEntry(StringsFile, id);

        return new StringUpdateResult
        {
            Success = true,
            UpdatedFields = updated,
            String = entry != null ? MapStringEntry(entry) : null
        };
    }

    [McpServerTool, Description("Delete a localization string by ID.")]
    public StringDeleteResult strings_delete(
        [Description("String ID to delete")]
        string id)
    {
        Log("Tool", $"strings_delete(id={id})");

        var entry = documentStore.GetEntry(StringsFile, id);
        if (entry == null)
        {
            return new StringDeleteResult
            {
                Success = false,
                Error = $"String '{id}' not found in {StringsFile}."
            };
        }

        var success = documentStore.DeleteEntry(StringsFile, id);

        return new StringDeleteResult
        {
            Success = success,
            DeletedId = success ? id : null,
            Error = success ? null : "Failed to delete string."
        };
    }

    [McpServerTool, Description("List all unique tag names used across all localization strings.")]
    public TagListResult strings_list_tags()
    {
        Log("Tool", "strings_list_tags()");

        var entries = documentStore.GetEntries(StringsFile);
        var allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var tags = entry.GetTagList("tags", "tag", "tag_name", "weight");
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var tag in tags.Split(',', ';'))
                {
                    var tagName = tag.Trim();
                    // Remove weight suffix if present
                    var parenIndex = tagName.IndexOf('(');
                    if (parenIndex > 0)
                    {
                        tagName = tagName.Substring(0, parenIndex);
                    }
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        allTags.Add(tagName);
                    }
                }
            }
        }

        return new TagListResult
        {
            Success = true,
            TagCount = allTags.Count,
            Tags = allTags.OrderBy(t => t).ToList()
        };
    }

    [McpServerTool, Description("Get all strings in a specific category and optionally subcategory. Use strings_list_categories first to see available categories.")]
    public StringsByCategoryResult strings_by_category(
        [Description("Category name (e.g., 'Skill perks', 'Career Choice', 'UI', 'Dialogue')")]
        string category,
        [Description("Optional subcategory to further filter (e.g., 'Faith', 'Grail Knight')")]
        string? subcategory = null,
        [Description("Include full text content in results (default false for browsing, set true when you need the actual text)")]
        bool include_text = false,
        [Description("Maximum number of results to return (default 100)")]
        int limit = 100)
    {
        Log("Tool", $"strings_by_category(category={category}, subcategory={subcategory ?? "null"}, include_text={include_text}, limit={limit})");

        var entries = documentStore.GetEntries(StringsFile);

        var matches = entries
            .Where(e =>
            {
                var cat = e.GetAttributeValue("category");
                if (!string.Equals(cat, category, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(subcategory))
                {
                    var subcat = e.GetAttributeValue("subcategory");
                    return string.Equals(subcat, subcategory, StringComparison.OrdinalIgnoreCase);
                }

                return true;
            })
            .Take(limit)
            .Select(e => MapStringEntry(e, include_text))
            .ToList();

        return new StringsByCategoryResult
        {
            Success = true,
            Category = category,
            Subcategory = subcategory,
            MatchCount = matches.Count,
            Strings = matches
        };
    }

    [McpServerTool, Description("List all categories and subcategories used across localization strings. Useful for discovering what string groups exist.")]
    public CategoryListResult strings_list_categories()
    {
        Log("Tool", "strings_list_categories()");

        var entries = documentStore.GetEntries(StringsFile);
        var categories = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var category = entry.GetAttributeValue("category");
            if (!string.IsNullOrWhiteSpace(category))
            {
                if (!categories.ContainsKey(category))
                {
                    categories[category] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var subcategory = entry.GetAttributeValue("subcategory");
                if (!string.IsNullOrWhiteSpace(subcategory))
                {
                    categories[category].Add(subcategory);
                }
            }
        }

        var result = categories
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new CategoryInfo
            {
                Category = kvp.Key,
                Subcategories = kvp.Value.OrderBy(s => s).ToList()
            })
            .ToList();

        return new CategoryListResult
        {
            Success = true,
            CategoryCount = result.Count,
            Categories = result
        };
    }

    [McpServerTool, Description("Get all strings that have a specific tag. Useful for finding conditional dialogue strings.")]
    public StringsByTagResult strings_by_tag(
        [Description("Tag name to filter by (e.g., 'IsOrcTag', 'EmpireTag', 'VampireMaleTag')")]
        string tag,
        [Description("Maximum number of results to return (default 50)")]
        int limit = 50)
    {
        Log("Tool", $"strings_by_tag(tag={tag}, limit={limit})");

        var entries = documentStore.GetEntries(StringsFile);
        var tagLower = tag.ToLowerInvariant();

        var matches = entries
            .Where(e =>
            {
                var tags = e.GetTagList("tags", "tag", "tag_name", "weight");
                return tags?.ToLowerInvariant().Contains(tagLower) == true;
            })
            .Take(limit)
            .Select(e => MapStringEntry(e, true))
            .ToList();

        return new StringsByTagResult
        {
            Success = true,
            Tag = tag,
            MatchCount = matches.Count,
            Strings = matches
        };
    }

    private static StringDto MapStringEntry(XmlEntry entry, bool includeText = true)
    {
        var textAttr = entry.GetAttribute("text");
        var tags = entry.GetTagList("tags", "tag", "tag_name", "weight");
        var category = entry.GetAttributeValue("category");
        var subcategory = entry.GetAttributeValue("subcategory");

        return new StringDto
        {
            Id = entry.Id ?? "",
            Text = includeText ? (textAttr?.DisplayValue ?? "") : null,
            LocalizationKey = includeText ? textAttr?.LocalizationKey : null,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            Subcategory = string.IsNullOrWhiteSpace(subcategory) ? null : subcategory,
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags
        };
    }
}

// Result DTOs

public class StringDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("localization_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalizationKey { get; set; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    [JsonPropertyName("subcategory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subcategory { get; set; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tags { get; set; }
}

public class StringGetResult
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("string")]
    public StringDto? String { get; set; }
}

public class StringQueryResult
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

    [JsonPropertyName("strings")]
    public List<StringDto>? Strings { get; set; }
}

public class StringSearchResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("match_count")]
    public int MatchCount { get; set; }

    [JsonPropertyName("strings")]
    public List<StringDto>? Strings { get; set; }
}

public class StringAddResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("string")]
    public StringDto? String { get; set; }
}

public class StringUpdateResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("updated_fields")]
    public List<string>? UpdatedFields { get; set; }

    [JsonPropertyName("string")]
    public StringDto? String { get; set; }
}

public class StringDeleteResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("deleted_id")]
    public string? DeletedId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class TagListResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("tag_count")]
    public int TagCount { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class StringsByTagResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("match_count")]
    public int MatchCount { get; set; }

    [JsonPropertyName("strings")]
    public List<StringDto>? Strings { get; set; }
}

public class CategoryInfo
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("subcategories")]
    public List<string> Subcategories { get; set; } = new();
}

public class StringsByCategoryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("subcategory")]
    public string? Subcategory { get; set; }

    [JsonPropertyName("match_count")]
    public int MatchCount { get; set; }

    [JsonPropertyName("strings")]
    public List<StringDto>? Strings { get; set; }
}

public class CategoryListResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("category_count")]
    public int CategoryCount { get; set; }

    [JsonPropertyName("categories")]
    public List<CategoryInfo> Categories { get; set; } = new();
}
