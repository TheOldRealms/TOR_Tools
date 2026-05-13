using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TORTools.Core.DocumentStore;
using TORTools.Core.Schema;

namespace TORTools.Mcp.Host.Tools;

/// <summary>
/// MCP tools for file and schema operations.
/// </summary>
[McpServerToolType]
public class FileTools(IDocumentStore documentStore)
{
    [McpServerTool, Description("List all available XML files in the TOR workspace, organized by category.")]
    public ListFilesResult list_files(
        [Description("Optional category filter (e.g., 'Item Catalog', 'Unit Catalog', 'Abilities & Effects')")]
        string? category = null)
    {
        var files = documentStore.GetAvailableFiles();

        if (!string.IsNullOrWhiteSpace(category))
        {
            files = files.Where(f =>
                f.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var grouped = files
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key)
            .Select(g => new FileCategoryGroup
            {
                Category = g.Key,
                Files = g.Select(f => new FileInfoDto
                {
                    FileName = f.FileName,
                    DisplayName = f.DisplayName,
                    Repository = f.Repository
                }).ToList()
            })
            .ToList();

        return new ListFilesResult
        {
            TotalFiles = files.Count,
            Categories = grouped
        };
    }

    [McpServerTool, Description("Get the schema definition for an XML file, including field types, enums, and validation rules.")]
    public GetSchemaResult get_schema(
        [Description("File name (e.g., 'tor_armors.xml', 'tor_meleeweapons.xml')")]
        string file)
    {
        var schema = documentStore.GetSchema(file);
        if (schema == null)
        {
            return new GetSchemaResult
            {
                Found = false,
                Error = $"No schema found for '{file}'. Use list_files to see available files."
            };
        }

        return new GetSchemaResult
        {
            Found = true,
            Schema = MapSchema(schema)
        };
    }

    private static SchemaDto MapSchema(SchemaDefinition schema)
    {
        return new SchemaDto
        {
            FileName = schema.FileName,
            DisplayName = schema.DisplayName,
            Description = schema.Description,
            RootElement = schema.RootElement,
            EntryElement = schema.EntryElement,
            Fields = schema.Fields.Select(kvp => MapField(kvp.Key, kvp.Value))
                .OrderBy(f => f.Order)
                .ToList()
        };
    }

    private static FieldDto MapField(string name, FieldDefinition field)
    {
        return new FieldDto
        {
            Name = name,
            DisplayName = field.DisplayName ?? name,
            Description = field.Description,
            Type = field.Type,
            Required = field.Required,
            ReadOnly = field.ReadOnly,
            Order = field.Order,
            Min = field.Min,
            Max = field.Max,
            Default = field.Default,
            EnumValues = field.EnumValues?.Select(e => new EnumValueDto
            {
                Value = e.Value,
                DisplayName = e.DisplayName,
                Description = e.Description
            }).ToList(),
            CrossReference = field.CrossReference != null ? new CrossRefDto
            {
                TargetFile = field.CrossReference.TargetFile,
                TargetKeyField = field.CrossReference.TargetKeyField,
                TargetDisplayField = field.CrossReference.TargetDisplayField
            } : null
        };
    }
}

// Result DTOs

public class ListFilesResult
{
    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("categories")]
    public List<FileCategoryGroup> Categories { get; set; } = new();
}

public class FileCategoryGroup
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("files")]
    public List<FileInfoDto> Files { get; set; } = new();
}

public class FileInfoDto
{
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "";
}

public class GetSchemaResult
{
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("schema")]
    public SchemaDto? Schema { get; set; }
}

public class SchemaDto
{
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("root_element")]
    public string RootElement { get; set; } = "";

    [JsonPropertyName("entry_element")]
    public string EntryElement { get; set; } = "";

    [JsonPropertyName("fields")]
    public List<FieldDto> Fields { get; set; } = new();
}

public class FieldDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("default")]
    public string? Default { get; set; }

    [JsonPropertyName("enum_values")]
    public List<EnumValueDto>? EnumValues { get; set; }

    [JsonPropertyName("cross_reference")]
    public CrossRefDto? CrossReference { get; set; }
}

public class EnumValueDto
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class CrossRefDto
{
    [JsonPropertyName("target_file")]
    public string TargetFile { get; set; } = "";

    [JsonPropertyName("target_key_field")]
    public string TargetKeyField { get; set; } = "";

    [JsonPropertyName("target_display_field")]
    public string? TargetDisplayField { get; set; }
}
