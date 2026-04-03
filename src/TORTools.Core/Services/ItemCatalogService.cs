using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and querying all item IDs from the various item catalog files.
/// Used for validating equipment set references.
/// </summary>
public class ItemCatalogService
{
    private readonly HashSet<string> _allItemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _itemIdsByFile = new();
    private bool _isLoaded;
    private readonly object _loadLock = new();

    /// <summary>
    /// The item catalog XML files to load.
    /// </summary>
    private static readonly (string FileName, string KeyAttribute)[] ItemFiles =
    {
        ("tor_items/tor_armors.xml", "id"),
        ("tor_items/tor_meleeweapons.xml", "id"),
        ("tor_items/tor_shields.xml", "id"),
        ("tor_items/tor_rangedweapons.xml", "id"),
        ("tor_items/tor_horseandharness.xml", "id"),
        ("tor_items/tor_projectiles.xml", "id"),
        ("tor_items/tor_other_items.xml", "id"),
    };

    /// <summary>
    /// Maps clipboard slot names to XML slot names.
    /// Clipboard format from CopyEquipmentToClipBoard: Weapon1,Weapon2,Weapon3,Weapon4,Helm,Torso,Cloak,Glove,Boot,Mount,MountArmor
    /// </summary>
    public static readonly Dictionary<int, string> ClipboardSlotToXmlSlot = new()
    {
        { 0, "Item0" },      // Weapon1
        { 1, "Item1" },      // Weapon2
        { 2, "Item2" },      // Weapon3
        { 3, "Item3" },      // Weapon4
        { 4, "Head" },       // Helm
        { 5, "Body" },       // Torso
        { 6, "Cape" },       // Cloak
        { 7, "Gloves" },     // Glove
        { 8, "Leg" },        // Boot
        { 9, "Horse" },      // Mount
        { 10, "HorseHarness" } // MountArmor
    };

    /// <summary>
    /// All valid XML equipment slot names.
    /// </summary>
    public static readonly string[] AllSlots =
    {
        "Item0", "Item1", "Item2", "Item3",
        "Head", "Body", "Cape", "Gloves", "Leg",
        "Horse", "HorseHarness"
    };

    /// <summary>
    /// Display names for equipment slots.
    /// </summary>
    public static readonly Dictionary<string, string> SlotDisplayNames = new()
    {
        { "Item0", "Weapon 1" },
        { "Item1", "Weapon 2" },
        { "Item2", "Weapon 3" },
        { "Item3", "Weapon 4" },
        { "Head", "Head" },
        { "Body", "Body" },
        { "Cape", "Cape" },
        { "Gloves", "Gloves" },
        { "Leg", "Leg" },
        { "Horse", "Horse" },
        { "HorseHarness", "Horse Harness" }
    };

    /// <summary>
    /// Loads all item IDs from the TOR_Armory module.
    /// </summary>
    /// <param name="armoryModuleDataPath">Path to TOR_Armory/ModuleData</param>
    public void LoadItems(string armoryModuleDataPath)
    {
        lock (_loadLock)
        {
            if (_isLoaded) return;

            _allItemIds.Clear();
            _itemIdsByFile.Clear();

            foreach (var (fileName, keyAttr) in ItemFiles)
            {
                var filePath = Path.Combine(armoryModuleDataPath, fileName);
                var ids = LoadItemsFromFile(filePath, keyAttr);
                _itemIdsByFile[fileName] = ids;

                foreach (var id in ids)
                {
                    _allItemIds.Add(id);
                }
            }

            Console.WriteLine($"[ItemCatalogService] Loaded {_allItemIds.Count} total item IDs from {_itemIdsByFile.Count} files");
            _isLoaded = true;
        }
    }

    /// <summary>
    /// Loads item IDs from a single XML file.
    /// </summary>
    private HashSet<string> LoadItemsFromFile(string filePath, string keyAttribute)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[ItemCatalogService] File not found: {filePath}");
            return result;
        }

        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return result;

            foreach (var entry in root.Elements())
            {
                var id = entry.Attribute(keyAttribute)?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
            }

            Console.WriteLine($"[ItemCatalogService] Loaded {result.Count} items from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ItemCatalogService] Error loading {filePath}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Checks if an item ID exists in the catalog.
    /// </summary>
    /// <param name="itemId">The item ID to check (with or without "Item." prefix)</param>
    /// <returns>True if the item exists</returns>
    public bool ItemExists(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return true; // Empty is valid (no item equipped)

        // Strip "Item." prefix if present
        var id = itemId.StartsWith("Item.", StringComparison.OrdinalIgnoreCase)
            ? itemId.Substring(5)
            : itemId;

        // "none" is valid (no item)
        if (id.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;

        return _allItemIds.Contains(id);
    }

    /// <summary>
    /// Gets all item IDs for autocomplete.
    /// </summary>
    /// <returns>All known item IDs</returns>
    public IReadOnlySet<string> GetAllItemIds()
    {
        return _allItemIds;
    }

    /// <summary>
    /// Parses clipboard text from CopyEquipmentToClipBoard.
    /// Format: "Item.{id}" or "none" for each slot, comma-separated.
    /// </summary>
    /// <param name="clipboardText">The clipboard text</param>
    /// <returns>Dictionary mapping slot names to item IDs (with Item. prefix stripped)</returns>
    public Dictionary<string, string> ParseClipboardEquipment(string clipboardText)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(clipboardText))
            return result;

        var parts = clipboardText.Split(',');
        for (int i = 0; i < parts.Length && i < ClipboardSlotToXmlSlot.Count; i++)
        {
            var value = parts[i].Trim();
            if (string.IsNullOrEmpty(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
                continue;

            var slotName = ClipboardSlotToXmlSlot[i];

            // Keep the Item. prefix for XML compatibility
            if (!value.StartsWith("Item.", StringComparison.OrdinalIgnoreCase))
                value = "Item." + value;

            result[slotName] = value;
        }

        return result;
    }

    /// <summary>
    /// Validates all item references in an equipment set.
    /// </summary>
    /// <param name="equipmentSlots">Dictionary of slot -> item ID</param>
    /// <returns>List of invalid item IDs</returns>
    public List<string> ValidateEquipmentSet(Dictionary<string, string> equipmentSlots)
    {
        var invalid = new List<string>();

        foreach (var (slot, itemId) in equipmentSlots)
        {
            if (!ItemExists(itemId))
            {
                invalid.Add(itemId);
            }
        }

        return invalid;
    }

    /// <summary>
    /// Clears the loaded items and forces a reload on next access.
    /// </summary>
    public void ClearCache()
    {
        lock (_loadLock)
        {
            _allItemIds.Clear();
            _itemIdsByFile.Clear();
            _isLoaded = false;
        }
    }

    /// <summary>
    /// Whether items have been loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;
}
