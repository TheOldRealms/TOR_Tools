using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Information about an item trait from the item traits file.
/// </summary>
public class ItemTraitInfo
{
    public string StringId { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconName { get; init; } = "";
    public string Description { get; init; } = "";
    public string ValidItemType { get; init; } = "";
}

/// <summary>
/// Service for loading and providing access to item trait information.
/// Used for displaying trait icons and names in cross-reference fields.
/// </summary>
public interface IItemTraitCatalogService
{
    /// <summary>
    /// Gets item trait info by string ID.
    /// </summary>
    ItemTraitInfo? GetTrait(string stringId);

    /// <summary>
    /// Gets the icon name for an item trait.
    /// </summary>
    string? GetTraitIcon(string stringId);

    /// <summary>
    /// Gets the description for an item trait.
    /// </summary>
    string? GetTraitDescription(string stringId);

    /// <summary>
    /// Gets all item trait IDs.
    /// </summary>
    IReadOnlyList<string> GetAllTraitIds();
}

/// <summary>
/// Service for loading and providing access to item trait information.
/// </summary>
public class ItemTraitCatalogService : IItemTraitCatalogService
{
    private readonly Dictionary<string, ItemTraitInfo> _traits = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _traitIds = new();

    public ItemTraitCatalogService(string moduleDataPath)
    {
        LoadTraits(moduleDataPath);
    }

    private void LoadTraits(string moduleDataPath)
    {
        var customXmlsPath = Path.Combine(moduleDataPath, "tor_custom_xmls");
        var traitFile = Path.Combine(customXmlsPath, "tor_itemtraits.xml");

        if (!File.Exists(traitFile))
        {
            Console.WriteLine($"[ItemTraitCatalogService] Item traits file not found: {traitFile}");
            return;
        }

        try
        {
            var doc = XDocument.Load(traitFile);
            var root = doc.Root;
            if (root == null) return;

            foreach (var element in root.Elements("ItemTrait"))
            {
                var stringId = element.Attribute("ItemTraitStringId")?.Value ?? "";
                if (string.IsNullOrEmpty(stringId)) continue;

                var info = new ItemTraitInfo
                {
                    StringId = stringId,
                    Name = element.Attribute("ItemTraitName")?.Value ?? stringId,
                    IconName = element.Attribute("IconName")?.Value ?? "",
                    Description = element.Element("ItemTraitDescription")?.Value ?? "",
                    ValidItemType = element.Element("ValidItemType")?.Value ?? ""
                };

                _traits[stringId] = info;
                _traitIds.Add(stringId);
            }

            _traitIds.Sort(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"[ItemTraitCatalogService] Loaded {_traits.Count} item traits");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemTraitCatalogService] Error loading item traits: {ex.Message}");
        }
    }

    public ItemTraitInfo? GetTrait(string stringId)
    {
        if (string.IsNullOrEmpty(stringId)) return null;
        return _traits.TryGetValue(stringId, out var info) ? info : null;
    }

    public string? GetTraitIcon(string stringId)
    {
        return GetTrait(stringId)?.IconName;
    }

    public string? GetTraitDescription(string stringId)
    {
        return GetTrait(stringId)?.Description;
    }

    public IReadOnlyList<string> GetAllTraitIds() => _traitIds;
}
