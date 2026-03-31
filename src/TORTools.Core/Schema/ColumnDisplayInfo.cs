namespace TORTools.Core.Schema;

/// <summary>
/// Display information for a column/attribute.
/// </summary>
public class ColumnDisplayInfo
{
    /// <summary>
    /// The actual XML attribute name (e.g., "SwingDamage").
    /// </summary>
    public required string AttributeName { get; init; }

    /// <summary>
    /// Human-readable display name (e.g., "Swing Damage").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional tooltip/description for the column.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Suggested column width in pixels.
    /// </summary>
    public int Width { get; init; } = 120;

    /// <summary>
    /// Whether this column should be read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Display order (lower = earlier).
    /// </summary>
    public int Order { get; init; } = 1000;

    /// <summary>
    /// Column group name for visual grouping (e.g., "Parts", "Stats").
    /// </summary>
    public string? Group { get; init; }
}

/// <summary>
/// Provides column display mappings for different XML file types.
/// </summary>
public static class ColumnDisplayMappings
{
    /// <summary>
    /// Common attribute display names used across many files.
    /// </summary>
    private static readonly Dictionary<string, ColumnDisplayInfo> CommonMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = new() { AttributeName = "id", DisplayName = "ID", Width = 220, IsReadOnly = false, Order = 1 },
        ["name"] = new() { AttributeName = "name", DisplayName = "Name", Width = 200, Order = 2 },
        ["culture"] = new() { AttributeName = "culture", DisplayName = "Culture", Width = 120, Order = 10 },
        ["is_merchandise"] = new() { AttributeName = "is_merchandise", DisplayName = "Merchandise", Width = 90, Order = 20 },
        ["value"] = new() { AttributeName = "value", DisplayName = "Value (Price)", Width = 100, Order = 21 },
        ["tier"] = new() { AttributeName = "tier", DisplayName = "Tier", Width = 50, Order = 15 },
        ["type"] = new() { AttributeName = "type", DisplayName = "Type", Width = 100, Order = 5 },
        ["subtype"] = new() { AttributeName = "subtype", DisplayName = "Subtype", Width = 100, Order = 6 },
    };

    /// <summary>
    /// Weapon-specific attribute mappings.
    /// </summary>
    private static readonly Dictionary<string, ColumnDisplayInfo> WeaponMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["crafting_template"] = new() { AttributeName = "crafting_template", DisplayName = "Crafting Template", Width = 180, Order = 30 },
        ["swing_damage"] = new() { AttributeName = "swing_damage", DisplayName = "Swing Damage", Width = 100, Order = 40, Group = "Stats" },
        ["thrust_damage"] = new() { AttributeName = "thrust_damage", DisplayName = "Thrust Damage", Width = 100, Order = 41, Group = "Stats" },
        ["weapon_length"] = new() { AttributeName = "weapon_length", DisplayName = "Length", Width = 70, Order = 42, Group = "Stats" },
        ["speed_rating"] = new() { AttributeName = "speed_rating", DisplayName = "Speed", Width = 70, Order = 43, Group = "Stats" },
        ["handling"] = new() { AttributeName = "handling", DisplayName = "Handling", Width = 80, Order = 44, Group = "Stats" },
        ["difficulty"] = new() { AttributeName = "difficulty", DisplayName = "Difficulty", Width = 80, Order = 50 },
        ["item_traits"] = new() { AttributeName = "item_traits", DisplayName = "Item Traits", Width = 150, Order = 60 },
        ["damage_type"] = new() { AttributeName = "damage_type", DisplayName = "Damage Type", Width = 120, Order = 61 },
    };

    /// <summary>
    /// Armor-specific attribute mappings.
    /// </summary>
    private static readonly Dictionary<string, ColumnDisplayInfo> ArmorMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["body_armor"] = new() { AttributeName = "body_armor", DisplayName = "Body Armor", Width = 90, Order = 40, Group = "Protection" },
        ["head_armor"] = new() { AttributeName = "head_armor", DisplayName = "Head Armor", Width = 90, Order = 41, Group = "Protection" },
        ["leg_armor"] = new() { AttributeName = "leg_armor", DisplayName = "Leg Armor", Width = 90, Order = 42, Group = "Protection" },
        ["arm_armor"] = new() { AttributeName = "arm_armor", DisplayName = "Arm Armor", Width = 90, Order = 43, Group = "Protection" },
        ["weight"] = new() { AttributeName = "weight", DisplayName = "Weight", Width = 70, Order = 50 },
        ["appearance"] = new() { AttributeName = "appearance", DisplayName = "Appearance", Width = 100, Order = 55 },
        ["material_type"] = new() { AttributeName = "material_type", DisplayName = "Material", Width = 100, Order = 56 },
    };

    /// <summary>
    /// Character/troop attribute mappings.
    /// </summary>
    private static readonly Dictionary<string, ColumnDisplayInfo> CharacterMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["is_hero"] = new() { AttributeName = "is_hero", DisplayName = "Is Hero", Width = 70, Order = 10 },
        ["is_female"] = new() { AttributeName = "is_female", DisplayName = "Female", Width = 70, Order = 11 },
        ["is_template"] = new() { AttributeName = "is_template", DisplayName = "Template", Width = 70, Order = 12 },
        ["default_group"] = new() { AttributeName = "default_group", DisplayName = "Formation", Width = 100, Order = 20 },
        ["occupation"] = new() { AttributeName = "occupation", DisplayName = "Occupation", Width = 100, Order = 21 },
        ["upgrade_targets"] = new() { AttributeName = "upgrade_targets", DisplayName = "Upgrades To", Width = 180, Order = 30 },
        ["level"] = new() { AttributeName = "level", DisplayName = "Level", Width = 50, Order = 15 },
    };

    /// <summary>
    /// Ability/effect attribute mappings.
    /// </summary>
    private static readonly Dictionary<string, ColumnDisplayInfo> AbilityMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ability_type"] = new() { AttributeName = "ability_type", DisplayName = "Ability Type", Width = 120, Order = 10 },
        ["cast_type"] = new() { AttributeName = "cast_type", DisplayName = "Cast Type", Width = 100, Order = 11 },
        ["target_type"] = new() { AttributeName = "target_type", DisplayName = "Target", Width = 100, Order = 12 },
        ["cooldown"] = new() { AttributeName = "cooldown", DisplayName = "Cooldown", Width = 80, Order = 20 },
        ["duration"] = new() { AttributeName = "duration", DisplayName = "Duration", Width = 80, Order = 21 },
        ["base_damage"] = new() { AttributeName = "base_damage", DisplayName = "Base Damage", Width = 100, Order = 30 },
        ["winds_of_magic_cost"] = new() { AttributeName = "winds_of_magic_cost", DisplayName = "Magic Cost", Width = 90, Order = 25 },
    };

    /// <summary>
    /// Gets display info for a column, checking file-specific mappings first, then common.
    /// </summary>
    public static ColumnDisplayInfo GetDisplayInfo(string attributeName, string? fileName = null)
    {
        // Check file-specific mappings based on filename
        var specificMappings = GetFileMappings(fileName);
        if (specificMappings != null && specificMappings.TryGetValue(attributeName, out var specific))
        {
            // Return with original attribute name preserved (case matters for data binding)
            return new ColumnDisplayInfo
            {
                AttributeName = attributeName, // Keep original case!
                DisplayName = specific.DisplayName,
                Description = specific.Description,
                Width = specific.Width,
                IsReadOnly = specific.IsReadOnly,
                Order = specific.Order,
                Group = specific.Group
            };
        }

        // Check common mappings
        if (CommonMappings.TryGetValue(attributeName, out var common))
        {
            // Return with original attribute name preserved (case matters for data binding)
            return new ColumnDisplayInfo
            {
                AttributeName = attributeName, // Keep original case!
                DisplayName = common.DisplayName,
                Description = common.Description,
                Width = common.Width,
                IsReadOnly = common.IsReadOnly,
                Order = common.Order,
                Group = common.Group
            };
        }

        // Generate default display info
        return new ColumnDisplayInfo
        {
            AttributeName = attributeName,
            DisplayName = FormatAttributeName(attributeName),
            Width = EstimateWidth(attributeName),
            Order = 500
        };
    }

    /// <summary>
    /// Gets all display info for columns, sorted by order.
    /// </summary>
    public static IEnumerable<ColumnDisplayInfo> GetOrderedDisplayInfo(IEnumerable<string> attributeNames, string? fileName = null)
    {
        return attributeNames
            .Select(attr => GetDisplayInfo(attr, fileName))
            .OrderBy(info => info.Order);
    }

    private static Dictionary<string, ColumnDisplayInfo>? GetFileMappings(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return null;

        var lowerName = fileName.ToLowerInvariant();

        if (lowerName.Contains("weapon") || lowerName.Contains("sword") ||
            lowerName.Contains("axe") || lowerName.Contains("mace"))
            return WeaponMappings;

        if (lowerName.Contains("armor") || lowerName.Contains("shield"))
            return ArmorMappings;

        if (lowerName.Contains("hero") || lowerName.Contains("troop") ||
            lowerName.Contains("character") || lowerName.Contains("lord"))
            return CharacterMappings;

        if (lowerName.Contains("ability") || lowerName.Contains("effect"))
            return AbilityMappings;

        return null;
    }

    /// <summary>
    /// Converts attribute_name or attributeName to "Attribute Name".
    /// </summary>
    public static string FormatAttributeName(string attributeName)
    {
        if (string.IsNullOrEmpty(attributeName))
            return attributeName;

        var result = new System.Text.StringBuilder();
        bool lastWasUnderscore = true; // Start true to capitalize first letter

        foreach (char c in attributeName)
        {
            if (c == '_')
            {
                result.Append(' ');
                lastWasUnderscore = true;
            }
            else if (char.IsUpper(c) && result.Length > 0 && !lastWasUnderscore)
            {
                // CamelCase: insert space before uppercase
                result.Append(' ');
                result.Append(c);
                lastWasUnderscore = false;
            }
            else
            {
                result.Append(lastWasUnderscore ? char.ToUpper(c) : c);
                lastWasUnderscore = false;
            }
        }

        return result.ToString();
    }

    private static int EstimateWidth(string attributeName)
    {
        var len = attributeName.Length;
        if (len <= 4) return 60;
        if (len <= 8) return 90;
        if (len <= 12) return 120;
        if (len <= 20) return 160;
        return 200;
    }
}
