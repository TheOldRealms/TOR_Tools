using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Information about an ability from the ability templates.
/// </summary>
public class AbilityInfo
{
    public string StringId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SpriteName { get; init; } = "";
    public string AbilityType { get; init; } = "";
}

/// <summary>
/// Service for loading and providing access to ability template information.
/// Used for displaying ability icons and names in cross-reference fields.
/// </summary>
public interface IAbilityCatalogService
{
    /// <summary>
    /// Gets ability info by string ID.
    /// </summary>
    AbilityInfo? GetAbility(string stringId);

    /// <summary>
    /// Gets the sprite name (icon) for an ability.
    /// </summary>
    string? GetAbilitySprite(string stringId);

    /// <summary>
    /// Gets all ability IDs.
    /// </summary>
    IReadOnlyList<string> GetAllAbilityIds();
}

/// <summary>
/// Service for loading and providing access to ability template information.
/// </summary>
public class AbilityCatalogService : IAbilityCatalogService
{
    private readonly Dictionary<string, AbilityInfo> _abilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _abilityIds = new();

    public AbilityCatalogService(string moduleDataPath)
    {
        LoadAbilities(moduleDataPath);
    }

    private void LoadAbilities(string moduleDataPath)
    {
        var customXmlsPath = Path.Combine(moduleDataPath, "tor_custom_xmls");
        var abilityFile = Path.Combine(customXmlsPath, "tor_abilitytemplates.xml");

        if (!File.Exists(abilityFile))
        {
            Console.WriteLine($"[AbilityCatalogService] Ability templates file not found: {abilityFile}");
            return;
        }

        try
        {
            var doc = XDocument.Load(abilityFile);
            var root = doc.Root;
            if (root == null) return;

            foreach (var element in root.Elements("AbilityTemplate"))
            {
                var stringId = element.Attribute("StringID")?.Value ?? "";
                if (string.IsNullOrEmpty(stringId)) continue;

                var info = new AbilityInfo
                {
                    StringId = stringId,
                    Name = element.Attribute("Name")?.Value ?? stringId,
                    SpriteName = element.Attribute("SpriteName")?.Value ?? "",
                    AbilityType = element.Attribute("AbilityType")?.Value ?? ""
                };

                _abilities[stringId] = info;
                _abilityIds.Add(stringId);
            }

            _abilityIds.Sort(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"[AbilityCatalogService] Loaded {_abilities.Count} abilities");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AbilityCatalogService] Error loading abilities: {ex.Message}");
        }
    }

    public AbilityInfo? GetAbility(string stringId)
    {
        if (string.IsNullOrEmpty(stringId)) return null;
        return _abilities.TryGetValue(stringId, out var info) ? info : null;
    }

    public string? GetAbilitySprite(string stringId)
    {
        return GetAbility(stringId)?.SpriteName;
    }

    public IReadOnlyList<string> GetAllAbilityIds() => _abilityIds;
}
