using System.Text.Json.Serialization;

namespace TORTools.Core.Schema;

/// <summary>
/// Schema definition for an XML file type, loaded from JSON.
/// </summary>
public class SchemaDefinition
{
    /// <summary>
    /// The XML file name this schema applies to (e.g., "tor_meleeweapons.xml").
    /// </summary>
    [JsonPropertyName("file")]
    public string FileName { get; set; } = "";

    /// <summary>
    /// Display name for the file (e.g., "Melee Weapons").
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Description of what this file contains.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The root element name in the XML (e.g., "Items").
    /// </summary>
    [JsonPropertyName("rootElement")]
    public string RootElement { get; set; } = "";

    /// <summary>
    /// The entry element name (e.g., "Item").
    /// </summary>
    [JsonPropertyName("entryElement")]
    public string EntryElement { get; set; } = "";

    /// <summary>
    /// Field definitions for this file type.
    /// </summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();

    /// <summary>
    /// Gets a field definition by name (case-insensitive).
    /// </summary>
    public FieldDefinition? GetField(string attributeName)
    {
        // Try exact match first
        if (Fields.TryGetValue(attributeName, out var field))
            return field;

        // Try case-insensitive match
        foreach (var kvp in Fields)
        {
            if (kvp.Key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }
}

/// <summary>
/// Definition of a single field/attribute in the schema.
/// </summary>
public class FieldDefinition
{
    /// <summary>
    /// Human-readable display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description/tooltip text for this field.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Data type: string, int, float, bool, enum, id_ref.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// Whether this field is required.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>
    /// Whether this field should be read-only in the UI.
    /// </summary>
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Display order (lower = earlier).
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; } = 500;

    /// <summary>
    /// Suggested column width in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; } = 120;

    /// <summary>
    /// Column group name for visual grouping.
    /// </summary>
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    /// <summary>
    /// For enum type: list of valid values.
    /// </summary>
    [JsonPropertyName("enumValues")]
    public List<EnumValue>? EnumValues { get; set; }

    /// <summary>
    /// For int/float type: minimum value.
    /// </summary>
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    /// <summary>
    /// For int/float type: maximum value.
    /// </summary>
    [JsonPropertyName("max")]
    public double? Max { get; set; }

    /// <summary>
    /// For id_ref type: the target file to validate against.
    /// </summary>
    [JsonPropertyName("refTarget")]
    public string? RefTarget { get; set; }

    /// <summary>
    /// Default value for new entries.
    /// </summary>
    [JsonPropertyName("default")]
    public string? Default { get; set; }

    /// <summary>
    /// Whether this field should be hidden from the UI.
    /// </summary>
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    /// <summary>
    /// If set, this field is auto-filled based on another field's value.
    /// The value is the source field name.
    /// </summary>
    [JsonPropertyName("autoFillFrom")]
    public string? AutoFillFrom { get; set; }

    /// <summary>
    /// Cross-reference configuration for fields that pull data from other files.
    /// </summary>
    [JsonPropertyName("crossReference")]
    public CrossReferenceConfig? CrossReference { get; set; }

    /// <summary>
    /// Regex pattern for validation. Non-matching values generate warnings.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    /// Custom warning message when pattern doesn't match.
    /// </summary>
    [JsonPropertyName("patternWarning")]
    public string? PatternWarning { get; set; }
}

/// <summary>
/// Configuration for cross-reference fields that pull data from other XML files.
/// </summary>
public class CrossReferenceConfig
{
    /// <summary>
    /// The XML file that contains the mapping (e.g., "tor_extendeditemproperties.xml").
    /// </summary>
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = "";

    /// <summary>
    /// The field in the source file that matches our local key (e.g., "ItemStringId").
    /// </summary>
    [JsonPropertyName("sourceKeyField")]
    public string SourceKeyField { get; set; } = "";

    /// <summary>
    /// XPath-like path to the values in the source file (e.g., "ItemTraits/ItemTrait").
    /// </summary>
    [JsonPropertyName("sourceValuePath")]
    public string SourceValuePath { get; set; } = "";

    /// <summary>
    /// The target file containing the definitions to navigate to (e.g., "tor_itemtraits.xml").
    /// Use this for single target, or use TargetFiles for multiple possibilities.
    /// </summary>
    [JsonPropertyName("targetFile")]
    public string TargetFile { get; set; } = "";

    /// <summary>
    /// Multiple target files to search when navigating (e.g., armors AND weapons).
    /// If set, these are searched in order until the entry is found.
    /// </summary>
    [JsonPropertyName("targetFiles")]
    public List<string>? TargetFiles { get; set; }

    /// <summary>
    /// The key field in the target file (e.g., "ItemTraitStringId").
    /// </summary>
    [JsonPropertyName("targetKeyField")]
    public string TargetKeyField { get; set; } = "";

    /// <summary>
    /// Gets all target files to search, combining TargetFile and TargetFiles.
    /// </summary>
    public IEnumerable<string> GetAllTargetFiles()
    {
        if (TargetFiles != null && TargetFiles.Count > 0)
        {
            return TargetFiles;
        }
        if (!string.IsNullOrEmpty(TargetFile))
        {
            return new[] { TargetFile };
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// The local field to use as the lookup key (e.g., "id").
    /// </summary>
    [JsonPropertyName("localKeyField")]
    public string LocalKeyField { get; set; } = "";
}

/// <summary>
/// An enum value with optional display name and description.
/// </summary>
public class EnumValue
{
    /// <summary>
    /// The actual value stored in XML.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>
    /// Display name (defaults to Value if not specified).
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what this value means.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
