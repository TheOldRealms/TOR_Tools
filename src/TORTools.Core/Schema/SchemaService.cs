using System.Text.Json;

namespace TORTools.Core.Schema;

/// <summary>
/// Service for loading and querying XML schema definitions.
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// Gets the schema for a specific XML file.
    /// </summary>
    SchemaDefinition? GetSchema(string fileName);

    /// <summary>
    /// Gets field definition for a specific attribute.
    /// </summary>
    FieldDefinition? GetFieldDefinition(string fileName, string attributeName);

    /// <summary>
    /// Gets all loaded schemas.
    /// </summary>
    IReadOnlyList<SchemaDefinition> GetAllSchemas();

    /// <summary>
    /// Reloads all schemas from disk.
    /// </summary>
    void ReloadSchemas();
}

/// <summary>
/// Service for loading and querying XML schema definitions.
/// </summary>
public class SchemaService : ISchemaService
{
    private readonly string _schemasPath;
    private readonly Dictionary<string, SchemaDefinition> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public SchemaService(string? schemasPath = null)
    {
        // Default to schemas folder relative to the app
        _schemasPath = schemasPath ?? FindSchemasPath();
        LoadSchemas();
    }

    private static string FindSchemasPath()
    {
        // Try to find schemas folder by walking up from current directory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var schemasDir = Path.Combine(dir.FullName, "schemas");
            if (Directory.Exists(schemasDir))
                return schemasDir;

            // Also check if we're in bin/Debug/net10.0 etc
            var parentSchemas = Path.Combine(dir.FullName, "..", "..", "..", "..", "schemas");
            if (Directory.Exists(parentSchemas))
                return Path.GetFullPath(parentSchemas);

            dir = dir.Parent;
        }

        // Fallback to current directory
        return Path.Combine(Directory.GetCurrentDirectory(), "schemas");
    }

    private void LoadSchemas()
    {
        _schemas.Clear();

        if (!Directory.Exists(_schemasPath))
        {
            Console.WriteLine($"[SchemaService] Schemas directory not found: {_schemasPath}");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_schemasPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var schema = JsonSerializer.Deserialize<SchemaDefinition>(json, JsonOptions);

                if (schema != null && !string.IsNullOrEmpty(schema.FileName))
                {
                    _schemas[schema.FileName] = schema;
                    Console.WriteLine($"[SchemaService] Loaded schema for {schema.FileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SchemaService] Error loading schema {file}: {ex.Message}");
            }
        }

        Console.WriteLine($"[SchemaService] Loaded {_schemas.Count} schemas from {_schemasPath}");
    }

    public SchemaDefinition? GetSchema(string fileName)
    {
        // Try exact match first
        if (_schemas.TryGetValue(fileName, out var schema))
            return schema;

        // Try without path
        var justFileName = Path.GetFileName(fileName);
        if (_schemas.TryGetValue(justFileName, out schema))
            return schema;

        return null;
    }

    public FieldDefinition? GetFieldDefinition(string fileName, string attributeName)
    {
        var schema = GetSchema(fileName);
        if (schema == null)
            return null;

        if (schema.Fields.TryGetValue(attributeName, out var field))
            return field;

        return null;
    }

    public IReadOnlyList<SchemaDefinition> GetAllSchemas()
    {
        return _schemas.Values.ToList();
    }

    public void ReloadSchemas()
    {
        LoadSchemas();
    }
}
