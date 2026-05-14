using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.DocumentStore;
using TORTools.Core.Models;

using static TORTools.Core.DocumentStore.StandaloneDocumentStore;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for CRUD operations on XML entries.
/// </summary>
[McpServerToolType]
public class EntryTools(IDocumentStore documentStore)
{
    [McpServerTool, Description("Get a single entry by its ID from an XML file.")]
    public GetEntryResult get_entry(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Entry ID to retrieve")]
        string id)
    {
        Log("Tool", $"get_entry(file={file}, id={id})");
        var entry = documentStore.GetEntry(file, id);
        if (entry == null)
        {
            // Provide helpful diagnostics
            var entries = documentStore.GetEntries(file);
            var sampleIds = entries.Take(5).Select(e => e.Id).ToList();
            var similarIds = entries
                .Where(e => e.Id != null && e.Id.Contains(id, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(e => e.Id)
                .ToList();

            return new GetEntryResult
            {
                Found = false,
                Error = $"Entry '{id}' not found in '{file}'. " +
                        $"File has {entries.Count} entries. " +
                        (similarIds.Count > 0
                            ? $"Similar IDs: {string.Join(", ", similarIds)}"
                            : $"Sample IDs: {string.Join(", ", sampleIds)}")
            };
        }

        return new GetEntryResult
        {
            Found = true,
            Entry = MapEntry(entry)
        };
    }

    [McpServerTool, Description("Create a new entry in an XML file by cloning from an existing entry. Always provide a templateId - TOR entries have many required fields.")]
    public CreateEntryResult create_entry(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("ID of existing entry to use as template (recommended - use query_entries to find a suitable template)")]
        string? templateId = null,
        [Description("Optional: Initial attribute values as JSON object (e.g., {\"name\": \"New Item\", \"culture\": \"empire\"})")]
        string? attributes = null)
    {
        Log("Tool", $"create_entry(file={file}, templateId={templateId ?? "null"}, attributes={attributes ?? "null"})");
        var newEntry = documentStore.CreateEntry(file, templateId);
        if (newEntry == null)
        {
            return new CreateEntryResult
            {
                Success = false,
                Error = $"Failed to create entry in '{file}'. File may not exist."
            };
        }

        // Apply initial attributes if provided
        if (!string.IsNullOrWhiteSpace(attributes))
        {
            try
            {
                var attrDict = JsonSerializer.Deserialize<Dictionary<string, string?>>(attributes);
                if (attrDict != null && newEntry.Id != null)
                {
                    documentStore.UpdateEntry(file, newEntry.Id, attrDict);
                    // Refresh entry to get updated values
                    newEntry = documentStore.GetEntry(file, newEntry.Id);
                }
            }
            catch (JsonException ex)
            {
                return new CreateEntryResult
                {
                    Success = false,
                    Error = $"Invalid attributes JSON: {ex.Message}"
                };
            }
        }

        return new CreateEntryResult
        {
            Success = true,
            Entry = MapEntry(newEntry!)
        };
    }

    [McpServerTool, Description("Update attributes of an existing entry. Changes are auto-saved.")]
    public UpdateEntryResult update_entry(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Entry ID to update")]
        string id,
        [Description("Attributes to update as JSON object (e.g., {\"armor_weight\": \"15\", \"culture\": \"khuzait\"})")]
        string attributes)
    {
        Log("Tool", $"update_entry(file={file}, id={id}, attributes={attributes})");
        Dictionary<string, string?>? attrDict;
        try
        {
            attrDict = JsonSerializer.Deserialize<Dictionary<string, string?>>(attributes);
        }
        catch (JsonException ex)
        {
            return new UpdateEntryResult
            {
                Success = false,
                Error = $"Invalid attributes JSON: {ex.Message}"
            };
        }

        if (attrDict == null || attrDict.Count == 0)
        {
            return new UpdateEntryResult
            {
                Success = false,
                Error = "No attributes provided to update."
            };
        }

        var success = documentStore.UpdateEntry(file, id, attrDict);
        if (!success)
        {
            return new UpdateEntryResult
            {
                Success = false,
                Error = $"Entry '{id}' not found in '{file}'."
            };
        }

        // Get updated entry
        var entry = documentStore.GetEntry(file, id);

        return new UpdateEntryResult
        {
            Success = true,
            UpdatedFields = attrDict.Keys.ToList(),
            Entry = entry != null ? MapEntry(entry) : null
        };
    }

    [McpServerTool, Description("Delete an entry by ID. Changes are auto-saved. This cannot be undone.")]
    public DeleteEntryResult delete_entry(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Entry ID to delete")]
        string id)
    {
        Log("Tool", $"delete_entry(file={file}, id={id})");
        var entry = documentStore.GetEntry(file, id);
        if (entry == null)
        {
            return new DeleteEntryResult
            {
                Success = false,
                Error = $"Entry '{id}' not found in '{file}'."
            };
        }

        var success = documentStore.DeleteEntry(file, id);

        return new DeleteEntryResult
        {
            Success = success,
            DeletedId = id,
            Error = success ? null : "Failed to delete entry."
        };
    }

    [McpServerTool, Description("Duplicate an existing entry with a new auto-generated ID.")]
    public DuplicateEntryResult duplicate_entry(
        [Description("File name (e.g., 'tor_armors.xml')")]
        string file,
        [Description("Entry ID to duplicate")]
        string id)
    {
        Log("Tool", $"duplicate_entry(file={file}, id={id})");
        var newEntry = documentStore.DuplicateEntry(file, id);
        if (newEntry == null)
        {
            // Provide helpful diagnostics
            var entries = documentStore.GetEntries(file);
            var sampleIds = entries.Take(5).Select(e => e.Id).ToList();
            var similarIds = entries
                .Where(e => e.Id != null && e.Id.Contains(id, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(e => e.Id)
                .ToList();

            return new DuplicateEntryResult
            {
                Success = false,
                Error = $"Entry '{id}' not found in '{file}'. " +
                        $"File has {entries.Count} entries. " +
                        (similarIds.Count > 0
                            ? $"Similar IDs: {string.Join(", ", similarIds)}"
                            : $"Sample IDs: {string.Join(", ", sampleIds)}")
            };
        }

        return new DuplicateEntryResult
        {
            Success = true,
            OriginalId = id,
            Entry = MapEntry(newEntry)
        };
    }

    private static EntryDto MapEntry(XmlEntry entry)
    {
        return new EntryDto
        {
            Id = entry.Id,
            Name = entry.Name,
            ElementName = entry.ElementName,
            Attributes = entry.Attributes.ToDictionary(
                a => a.Name,
                a => (string?)a.DisplayValue),
            Children = entry.Children.Count > 0
                ? entry.Children.Select(MapEntry).ToList()
                : null
        };
    }
}

// Result DTOs

public class GetEntryResult
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("entry")]
    public EntryDto? Entry { get; set; }
}

public class CreateEntryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("entry")]
    public EntryDto? Entry { get; set; }
}

public class UpdateEntryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("updated_fields")]
    public List<string>? UpdatedFields { get; set; }

    [JsonPropertyName("entry")]
    public EntryDto? Entry { get; set; }
}

public class DeleteEntryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("deleted_id")]
    public string? DeletedId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class DuplicateEntryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("original_id")]
    public string? OriginalId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("entry")]
    public EntryDto? Entry { get; set; }
}

public class EntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("element_name")]
    public string ElementName { get; set; } = "";

    [JsonPropertyName("attributes")]
    public Dictionary<string, string?> Attributes { get; set; } = new();

    [JsonPropertyName("children")]
    public List<EntryDto>? Children { get; set; }
}
