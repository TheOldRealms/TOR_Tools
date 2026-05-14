namespace TORTools.Core.Services;

/// <summary>
/// Service for inferring categories from string IDs.
/// </summary>
public static class StringCategoryService
{
    /// <summary>
    /// Category rules ordered by priority (more specific patterns first).
    /// </summary>
    private static readonly (string Pattern, string Category)[] CategoryRules =
    [
        // UI
        ("str_career_screen", "UI - Career"),
        ("str_inventory", "UI - Inventory"),
        ("tor_inquiry_", "UI - Dialogs"),
        ("tor_ink_", "UI - Dialogs"),

        // Character Creation
        ("tor_cc_", "Character Creation"),

        // Skills & Perks
        ("tor_skill_effect_", "Skill Effects"),
        ("tor_skill_", "Skills"),
        ("tor_perk_", "Perks"),

        // Abilities & Spells
        ("tor_ability_effect_type", "Ability Types"),
        ("tor_ability_", "Abilities"),
        ("tor_spell_stat_", "Spells - Stats"),
        ("tor_spell_", "Spells"),
        ("tor_spellcasting_level", "Spells - Levels"),
        ("tor_spellbook_", "Spellbook"),
        ("tor_career_ability_", "Career Abilities"),

        // Prayers & Religion
        ("tor_prayer_stat_", "Prayers - Stats"),
        ("tor_prayer_level", "Prayers - Levels"),
        ("tor_prayer_", "Prayers"),
        ("tor_prayerbook_", "Prayerbook"),
        ("tor_priest_", "Priests & Shrines"),

        // Traits
        ("tor_spellcaster_trait_", "Traits - Spellcaster"),
        ("tor_gunner_trait_", "Traits - Gunner"),
        ("_trait_name", "Traits"),
        ("_trait_description", "Traits"),

        // Races
        ("tor_race_name", "Races"),

        // Quests
        ("tor_quest_", "Quests"),

        // Greenskins
        ("tor_waaagh_", "Greenskins - Waaagh"),
        ("tor_greenskin_", "Greenskins"),
        ("tor_hideout_greenskin", "Greenskins - Hideouts"),

        // Vampires & Undead
        ("tor_vampire_", "Vampires"),
        ("tor_undead_", "Undead"),

        // Wood Elves
        ("tor_forest_harmony_", "Wood Elves"),

        // Stats & Combat
        ("tor_stats_", "Stats"),
        ("tor_healing_", "Healing"),
        ("tor_cooldown_", "Combat"),
        ("tor_stealth_", "Combat"),
        ("tor_brawl_", "Combat"),
        ("tor_joust_", "Combat"),

        // Settlements & Campaigns
        ("tor_chaos_rebellion_", "Campaigns"),
        ("tor_hideout_", "Hideouts"),

        // Items
        ("tor_item_", "Items"),

        // Generic/Misc
        ("tor_generic_", "Generic"),
        ("tor_magister_", "Empire - Magisters"),
        ("tor_not_enough_", "UI - Messages"),
    ];

    /// <summary>
    /// Infers a category from a string ID based on naming patterns.
    /// </summary>
    public static string? InferCategory(string? stringId)
    {
        if (string.IsNullOrEmpty(stringId))
            return null;

        var idLower = stringId.ToLowerInvariant();

        foreach (var (pattern, category) in CategoryRules)
        {
            if (idLower.Contains(pattern.ToLowerInvariant()))
                return category;
        }

        // Fallback: try to extract from ID structure
        // e.g., "tor_xxx_yyy" -> "Xxx"
        if (stringId.StartsWith("tor_"))
        {
            var parts = stringId.Split('_');
            if (parts.Length >= 2)
            {
                var firstPart = parts[1];
                // Capitalize first letter
                if (firstPart.Length > 0)
                    return char.ToUpper(firstPart[0]) + firstPart.Substring(1);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all known categories in display order.
    /// </summary>
    public static IReadOnlyList<string> GetAllCategories()
    {
        return CategoryRules
            .Select(r => r.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }
}
