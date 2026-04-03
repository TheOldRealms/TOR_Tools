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
    /// If true (default), write all attributes on a single line (compact format).
    /// If false, write each attribute on its own line (multi-line format).
    /// </summary>
    [JsonPropertyName("compactFormat")]
    public bool CompactFormat { get; set; } = true;

    /// <summary>
    /// Whether this file type has nested variation elements (e.g., equipment sets).
    /// </summary>
    [JsonPropertyName("hasNestedVariations")]
    public bool HasNestedVariations { get; set; }

    /// <summary>
    /// The element name for variations (e.g., "EquipmentSet").
    /// </summary>
    [JsonPropertyName("variationElement")]
    public string? VariationElement { get; set; }

    /// <summary>
    /// The element name for equipment items within variations (e.g., "Equipment").
    /// </summary>
    [JsonPropertyName("equipmentItemElement")]
    public string? EquipmentItemElement { get; set; }

    /// <summary>
    /// Whether items should be validated against the item catalog.
    /// </summary>
    [JsonPropertyName("itemCatalogCrossRef")]
    public bool ItemCatalogCrossRef { get; set; }

    /// <summary>
    /// Equipment slot definitions for equipment set files.
    /// </summary>
    [JsonPropertyName("equipmentSlots")]
    public List<EquipmentSlotDefinition>? EquipmentSlots { get; set; }

    /// <summary>
    /// Attributes that can appear on variation elements.
    /// </summary>
    [JsonPropertyName("variationAttributes")]
    public List<VariationAttributeDefinition>? VariationAttributes { get; set; }

    /// <summary>
    /// Configuration for a linked file that provides additional data for entries.
    /// The linked file's entries are matched by ID and merged into the main file's rows.
    /// </summary>
    [JsonPropertyName("linkedFile")]
    public LinkedFileConfig? LinkedFile { get; set; }

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
    /// Tuple list configuration for fields that edit multiple attribute tuples (e.g., DamageProportions).
    /// </summary>
    [JsonPropertyName("tupleList")]
    public TupleListConfig? TupleList { get; set; }

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

    /// <summary>
    /// Whether this field is nested (stored in a child element rather than as an attribute).
    /// </summary>
    [JsonPropertyName("nested")]
    public bool Nested { get; set; }

    /// <summary>
    /// Path to the nested value. Formats:
    /// - "ChildElement" - text content of child element
    /// - "ChildElement/@AttributeName" - attribute on child element
    /// </summary>
    [JsonPropertyName("nestedPath")]
    public string? NestedPath { get; set; }

    /// <summary>
    /// Prefix to strip from values for display (e.g., "NPCCharacter." for upgrade targets).
    /// </summary>
    [JsonPropertyName("prefixToStrip")]
    public string? PrefixToStrip { get; set; }

    /// <summary>
    /// Prefix to automatically add when saving values.
    /// </summary>
    [JsonPropertyName("prefixToAdd")]
    public string? PrefixToAdd { get; set; }

    /// <summary>
    /// If true, this field's value comes from a linked file rather than the main file.
    /// </summary>
    [JsonPropertyName("linkedField")]
    public bool LinkedField { get; set; }

    /// <summary>
    /// Path to the value in the linked file's entry (e.g., "Attributes", "ResourceCost/@UpkeepCost").
    /// </summary>
    [JsonPropertyName("linkedSource")]
    public string? LinkedSource { get; set; }

    /// <summary>
    /// Delimiter for joining/splitting list values (e.g., ":" for colon-separated attributes).
    /// </summary>
    [JsonPropertyName("delimiter")]
    public string? Delimiter { get; set; }
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

    /// <summary>
    /// Controls how the value is rendered/edited:
    /// - "crossRef" (default): Clickable links with navigation to target entries
    /// - "enum": Dropdown using field's enumValues
    /// - "int": Integer input field
    /// - "string": Simple text input
    /// When not "crossRef", the value is just read/written to the source file without navigation features.
    /// </summary>
    [JsonPropertyName("valueType")]
    public string ValueType { get; set; } = "crossRef";

    /// <summary>
    /// Prefix to strip from values before looking up (e.g., "SkillSet." for skill_template).
    /// Also used for display - values are shown without this prefix.
    /// </summary>
    [JsonPropertyName("prefixToStrip")]
    public string? PrefixToStrip { get; set; }

    /// <summary>
    /// Prefix to automatically add when saving values (e.g., "SkillSet." for skill_template).
    /// If set, values entered without this prefix will have it added automatically.
    /// </summary>
    [JsonPropertyName("prefixToAdd")]
    public string? PrefixToAdd { get; set; }
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

/// <summary>
/// Definition of an equipment slot for equipment set files.
/// </summary>
public class EquipmentSlotDefinition
{
    /// <summary>
    /// The slot attribute value (e.g., "Item0", "Head", "Body").
    /// </summary>
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "";

    /// <summary>
    /// Display name for the slot.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Display order.
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>
    /// Column width in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; } = 200;
}

/// <summary>
/// Definition of an attribute on variation elements.
/// </summary>
public class VariationAttributeDefinition
{
    /// <summary>
    /// The attribute name (e.g., "civilian").
    /// </summary>
    [JsonPropertyName("attribute")]
    public string Attribute { get; set; } = "";

    /// <summary>
    /// Display name for the attribute.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Data type (string, bool, int, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// Description of this attribute.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Configuration for a linked file that provides additional data merged into main file entries.
/// </summary>
public class LinkedFileConfig
{
    /// <summary>
    /// The linked file name (e.g., "tor_extendedunitproperties.xml").
    /// </summary>
    [JsonPropertyName("file")]
    public string FileName { get; set; } = "";

    /// <summary>
    /// Relative path to the linked file from the module's ModuleData folder.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// The root element name in the linked XML.
    /// </summary>
    [JsonPropertyName("rootElement")]
    public string RootElement { get; set; } = "";

    /// <summary>
    /// The entry element name in the linked XML.
    /// </summary>
    [JsonPropertyName("entryElement")]
    public string EntryElement { get; set; } = "";

    /// <summary>
    /// The field used to link entries between files (usually "id").
    /// </summary>
    [JsonPropertyName("linkField")]
    public string LinkField { get; set; } = "id";
}

/// <summary>
/// Configuration for tuple list fields that display/edit multiple attribute tuples.
/// Used for fields like DamageProportions, Resistances, DamageAmplifiers.
/// </summary>
public class TupleListConfig
{
    /// <summary>
    /// The source XML file containing the tuple data (e.g., "tor_extendedunitproperties.xml").
    /// </summary>
    [JsonPropertyName("sourceFile")]
    public string SourceFile { get; set; } = "";

    /// <summary>
    /// The key field in the source file that matches our local key (e.g., "id").
    /// </summary>
    [JsonPropertyName("sourceKeyField")]
    public string SourceKeyField { get; set; } = "id";

    /// <summary>
    /// Path to the container element (e.g., "DamageProportions").
    /// </summary>
    [JsonPropertyName("containerPath")]
    public string ContainerPath { get; set; } = "";

    /// <summary>
    /// Name of each tuple element (e.g., "DamageProportionTuple").
    /// </summary>
    [JsonPropertyName("elementName")]
    public string ElementName { get; set; } = "";

    /// <summary>
    /// The local field to use as the lookup key (e.g., "id").
    /// </summary>
    [JsonPropertyName("localKeyField")]
    public string LocalKeyField { get; set; } = "id";

    /// <summary>
    /// Column definitions for the tuple editor.
    /// </summary>
    [JsonPropertyName("columns")]
    public List<TupleColumnConfig> Columns { get; set; } = new();
}

/// <summary>
/// Configuration for a single column in a tuple editor.
/// </summary>
public class TupleColumnConfig
{
    /// <summary>
    /// The XML attribute name (e.g., "DamageType", "Percent").
    /// </summary>
    [JsonPropertyName("attribute")]
    public string Attribute { get; set; } = "";

    /// <summary>
    /// Display name for the column header.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Column type: "enum", "number", "string".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// For enum columns, the allowed values.
    /// </summary>
    [JsonPropertyName("enumValues")]
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// Column width in pixels.
    /// </summary>
    [JsonPropertyName("width")]
    public int Width { get; set; } = 100;
}
