using System.Text.Json;
using System.Xml.Linq;

namespace TORTools.Core.Services;

/// <summary>
/// Service for loading and querying all faction IDs (clans, kingdoms, cultures) from TOR_Core.
/// Used for validating faction references and providing autocomplete.
/// </summary>
public class FactionCatalogService
{
    private readonly HashSet<string> _clanIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _kingdomIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cultureIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _bannerKeyToImageName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _kingdomColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cultureColors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Culture colors loaded from JSON config file (data/culture_colors.json).
    /// </summary>
    private readonly Dictionary<string, string> _configuredCultureColors = new(StringComparer.OrdinalIgnoreCase);

    private bool _isLoaded;
    private readonly object _loadLock = new();
    private string? _bannerIconsBasePath;

    /// <summary>
    /// Loads all faction IDs from the TOR_Core module.
    /// </summary>
    /// <param name="coreModuleDataPath">Path to TOR_Core/ModuleData</param>
    /// <param name="armoryAssetSourcesPath">Path to TOR_Armory/AssetSources for banner images</param>
    public void LoadFactions(string coreModuleDataPath, string? armoryAssetSourcesPath = null)
    {
        lock (_loadLock)
        {
            if (_isLoaded) return;

            _clanIds.Clear();
            _kingdomIds.Clear();
            _cultureIds.Clear();
            _bannerKeyToImageName.Clear();

            // Set banner icons base path
            if (!string.IsNullOrEmpty(armoryAssetSourcesPath))
            {
                _bannerIconsBasePath = Path.Combine(armoryAssetSourcesPath, "extra_assets", "ui", "faction_banner_icons");
            }

            // Load clans (Factions)
            var clansPath = Path.Combine(coreModuleDataPath, "tor_clans.xml");
            LoadClansFromFile(clansPath);

            // Load kingdoms
            var kingdomsPath = Path.Combine(coreModuleDataPath, "tor_kingdoms.xml");
            LoadKingdomsFromFile(kingdomsPath);

            // Load cultures
            var culturesPath = Path.Combine(coreModuleDataPath, "tor_cultures.xml");
            LoadCulturesFromFile(culturesPath);

            // Load culture colors from JSON config
            // Path is: TOR_Core/ModuleData -> ../../TORTools/data/culture_colors.json
            var modulesPath = Path.GetDirectoryName(Path.GetDirectoryName(coreModuleDataPath));
            if (!string.IsNullOrEmpty(modulesPath))
            {
                var cultureColorsPath = Path.Combine(modulesPath, "TORTools", "data", "culture_colors.json");
                LoadCultureColorsFromJson(cultureColorsPath);
            }

            Console.WriteLine($"[FactionCatalogService] Loaded {_clanIds.Count} clans, {_kingdomIds.Count} kingdoms, {_cultureIds.Count} cultures");
            _isLoaded = true;
        }
    }

    private void LoadClansFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[FactionCatalogService] File not found: {filePath}");
            return;
        }

        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return;

            foreach (var entry in root.Elements("Faction"))
            {
                var id = entry.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    _clanIds.Add(id);

                    // Extract banner image name from banner_key
                    var bannerKey = entry.Attribute("banner_key")?.Value;
                    if (!string.IsNullOrEmpty(bannerKey))
                    {
                        var imageName = ExtractBannerImageName(bannerKey);
                        if (!string.IsNullOrEmpty(imageName))
                        {
                            _bannerKeyToImageName[id] = imageName;
                        }
                    }
                }
            }

            Console.WriteLine($"[FactionCatalogService] Loaded {_clanIds.Count} clans from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FactionCatalogService] Error loading {filePath}: {ex.Message}");
        }
    }

    private void LoadKingdomsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[FactionCatalogService] File not found: {filePath}");
            return;
        }

        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return;

            foreach (var entry in root.Elements("Kingdom"))
            {
                var id = entry.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    _kingdomIds.Add(id);

                    // Extract banner image name from banner_key
                    var bannerKey = entry.Attribute("banner_key")?.Value;
                    if (!string.IsNullOrEmpty(bannerKey))
                    {
                        var imageName = ExtractBannerImageName(bannerKey);
                        if (!string.IsNullOrEmpty(imageName))
                        {
                            _bannerKeyToImageName[$"kingdom_{id}"] = imageName;
                        }
                    }

                    // Store kingdom color for inheritance by clans
                    // Use primary_banner_color, but fall back to secondary if primary is too dark
                    var primaryColor = entry.Attribute("primary_banner_color")?.Value;
                    var secondaryColor = entry.Attribute("secondary_banner_color")?.Value;

                    // Check if primary color is too dark (luminance check)
                    var colorToUse = primaryColor;
                    if (!string.IsNullOrEmpty(primaryColor) && IsColorTooDark(primaryColor))
                    {
                        colorToUse = secondaryColor;
                    }

                    if (!string.IsNullOrEmpty(colorToUse))
                    {
                        _kingdomColors[id] = colorToUse;

                        // Also store culture -> color mapping (first kingdom wins)
                        var culture = entry.Attribute("culture")?.Value;
                        if (!string.IsNullOrEmpty(culture))
                        {
                            // Strip "Culture." prefix if present
                            var cultureId = culture.StartsWith("Culture.", StringComparison.OrdinalIgnoreCase)
                                ? culture.Substring(8)
                                : culture;

                            // First kingdom with this culture sets the color
                            if (!_cultureColors.ContainsKey(cultureId))
                            {
                                _cultureColors[cultureId] = colorToUse;
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[FactionCatalogService] Loaded {_kingdomIds.Count} kingdoms, {_cultureColors.Count} culture colors from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FactionCatalogService] Error loading {filePath}: {ex.Message}");
        }
    }

    private void LoadCulturesFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[FactionCatalogService] File not found: {filePath}");
            return;
        }

        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root == null) return;

            foreach (var entry in root.Elements("Culture"))
            {
                var id = entry.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    _cultureIds.Add(id);
                }
            }

            Console.WriteLine($"[FactionCatalogService] Loaded {_cultureIds.Count} cultures from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FactionCatalogService] Error loading {filePath}: {ex.Message}");
        }
    }

    private void LoadCultureColorsFromJson(string filePath)
    {
        _configuredCultureColors.Clear();

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[FactionCatalogService] Culture colors file not found: {filePath}");
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("colors", out var colorsElement))
            {
                foreach (var prop in colorsElement.EnumerateObject())
                {
                    var cultureId = prop.Name;
                    var color = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(color))
                    {
                        _configuredCultureColors[cultureId] = color;
                    }
                }
            }

            Console.WriteLine($"[FactionCatalogService] Loaded {_configuredCultureColors.Count} culture colors from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FactionCatalogService] Error loading culture colors: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a color value is too dark to be visible as a background.
    /// </summary>
    private static bool IsColorTooDark(string colorValue)
    {
        // Parse color value (formats: "0xffRRGGBB", "FFRRGGBB", "#RRGGBB")
        var hex = colorValue.Replace("0x", "").Replace("#", "").TrimStart('f', 'F');
        if (hex.Length >= 6)
        {
            // Take the last 6 characters (RGB portion)
            hex = hex.Substring(hex.Length - 6);
            if (int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                // Calculate relative luminance (simplified)
                var luminance = (0.299 * r + 0.587 * g + 0.114 * b);
                // If luminance is below ~50, consider it too dark
                return luminance < 50;
            }
        }
        return false;
    }

    /// <summary>
    /// Extracts the banner image name from a banner_key string.
    /// Banner keys have format: "11.116.149...:kingdom_averland"
    /// Returns the part after the colon (e.g., "kingdom_averland").
    /// </summary>
    public static string? ExtractBannerImageName(string? bannerKey)
    {
        if (string.IsNullOrEmpty(bannerKey))
            return null;

        var colonIndex = bannerKey.LastIndexOf(':');
        if (colonIndex >= 0 && colonIndex < bannerKey.Length - 1)
        {
            return bannerKey.Substring(colonIndex + 1);
        }

        return null;
    }

    /// <summary>
    /// Gets the full path to a banner image file for the given banner key.
    /// </summary>
    /// <param name="bannerKey">The banner_key attribute value</param>
    /// <returns>Full path to the PNG file, or null if not found</returns>
    public string? GetBannerImagePath(string? bannerKey)
    {
        if (string.IsNullOrEmpty(_bannerIconsBasePath))
            return null;

        var imageName = ExtractBannerImageName(bannerKey);
        if (string.IsNullOrEmpty(imageName))
            return null;

        var imagePath = Path.Combine(_bannerIconsBasePath, $"{imageName}.png");
        return File.Exists(imagePath) ? imagePath : null;
    }

    /// <summary>
    /// Gets the banner icons base path.
    /// </summary>
    public string? BannerIconsBasePath => _bannerIconsBasePath;

    /// <summary>
    /// Gets the color for a kingdom by ID.
    /// Used for clan color inheritance when clans don't have their own color.
    /// </summary>
    /// <param name="kingdomId">Kingdom ID (with or without "Kingdom." prefix)</param>
    /// <returns>The kingdom color, or null if not found</returns>
    public string? GetKingdomColor(string? kingdomId)
    {
        if (string.IsNullOrEmpty(kingdomId))
            return null;

        // Strip "Kingdom." prefix if present
        var id = kingdomId.StartsWith("Kingdom.", StringComparison.OrdinalIgnoreCase)
            ? kingdomId.Substring(8)
            : kingdomId;

        return _kingdomColors.TryGetValue(id, out var color) ? color : null;
    }

    /// <summary>
    /// Gets the color for a culture by ID.
    /// Uses JSON-configured colors first, falls back to extracted kingdom colors.
    /// </summary>
    /// <param name="cultureId">Culture ID (with or without "Culture." prefix)</param>
    /// <returns>The culture color, or null if not found</returns>
    public string? GetCultureColor(string? cultureId)
    {
        if (string.IsNullOrEmpty(cultureId))
            return null;

        // Strip "Culture." prefix if present
        var id = cultureId.StartsWith("Culture.", StringComparison.OrdinalIgnoreCase)
            ? cultureId.Substring(8)
            : cultureId;

        // Try JSON-configured colors first
        if (_configuredCultureColors.TryGetValue(id, out var configuredColor))
            return configuredColor;

        // Fall back to extracted kingdom colors
        return _cultureColors.TryGetValue(id, out var color) ? color : null;
    }

    /// <summary>
    /// Checks if a clan ID exists in the catalog.
    /// </summary>
    /// <param name="clanId">The clan ID to check (with or without "Faction." prefix)</param>
    /// <returns>True if the clan exists</returns>
    public bool ClanExists(string? clanId)
    {
        if (string.IsNullOrWhiteSpace(clanId))
            return true; // Empty is valid (no reference)

        // Strip "Faction." prefix if present
        var id = clanId.StartsWith("Faction.", StringComparison.OrdinalIgnoreCase)
            ? clanId.Substring(8)
            : clanId;

        return _clanIds.Contains(id);
    }

    /// <summary>
    /// Checks if a kingdom ID exists in the catalog.
    /// </summary>
    /// <param name="kingdomId">The kingdom ID to check (with or without "Kingdom." prefix)</param>
    /// <returns>True if the kingdom exists</returns>
    public bool KingdomExists(string? kingdomId)
    {
        if (string.IsNullOrWhiteSpace(kingdomId))
            return true; // Empty is valid (no reference)

        // Strip "Kingdom." prefix if present
        var id = kingdomId.StartsWith("Kingdom.", StringComparison.OrdinalIgnoreCase)
            ? kingdomId.Substring(8)
            : kingdomId;

        return _kingdomIds.Contains(id);
    }

    /// <summary>
    /// Checks if a culture ID exists in the catalog.
    /// </summary>
    /// <param name="cultureId">The culture ID to check (with or without "Culture." prefix)</param>
    /// <returns>True if the culture exists</returns>
    public bool CultureExists(string? cultureId)
    {
        if (string.IsNullOrWhiteSpace(cultureId))
            return true; // Empty is valid (no reference)

        // Strip "Culture." prefix if present
        var id = cultureId.StartsWith("Culture.", StringComparison.OrdinalIgnoreCase)
            ? cultureId.Substring(8)
            : cultureId;

        return _cultureIds.Contains(id);
    }

    /// <summary>
    /// Gets all clan IDs for autocomplete.
    /// </summary>
    /// <returns>All known clan IDs (without prefix)</returns>
    public IReadOnlySet<string> GetAllClans() => _clanIds;

    /// <summary>
    /// Gets all kingdom IDs for autocomplete.
    /// </summary>
    /// <returns>All known kingdom IDs (without prefix)</returns>
    public IReadOnlySet<string> GetAllKingdoms() => _kingdomIds;

    /// <summary>
    /// Gets all culture IDs for autocomplete.
    /// </summary>
    /// <returns>All known culture IDs (without prefix)</returns>
    public IReadOnlySet<string> GetAllCultures() => _cultureIds;

    /// <summary>
    /// Gets all clan IDs with "Faction." prefix for autocomplete.
    /// </summary>
    public IEnumerable<string> GetAllClansPrefixed() => _clanIds.Select(id => $"Faction.{id}");

    /// <summary>
    /// Gets all kingdom IDs with "Kingdom." prefix for autocomplete.
    /// </summary>
    public IEnumerable<string> GetAllKingdomsPrefixed() => _kingdomIds.Select(id => $"Kingdom.{id}");

    /// <summary>
    /// Gets all culture IDs with "Culture." prefix for autocomplete.
    /// </summary>
    public IEnumerable<string> GetAllCulturesPrefixed() => _cultureIds.Select(id => $"Culture.{id}");

    /// <summary>
    /// Clears the loaded factions and forces a reload on next access.
    /// </summary>
    public void ClearCache()
    {
        lock (_loadLock)
        {
            _clanIds.Clear();
            _kingdomIds.Clear();
            _cultureIds.Clear();
            _bannerKeyToImageName.Clear();
            _kingdomColors.Clear();
            _cultureColors.Clear();
            _configuredCultureColors.Clear();
            _isLoaded = false;
        }
    }

    /// <summary>
    /// Whether factions have been loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;
}
