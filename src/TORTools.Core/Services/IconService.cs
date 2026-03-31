using System.Collections.Concurrent;

namespace TORTools.Core.Services;

/// <summary>
/// Information about a discovered icon.
/// </summary>
public class IconInfo
{
    public string Name { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Category { get; init; } = "";
}

/// <summary>
/// Service for discovering and providing access to icon images.
/// </summary>
public interface IIconService
{
    /// <summary>
    /// Gets all discovered icons.
    /// </summary>
    IReadOnlyList<IconInfo> GetAllIcons();

    /// <summary>
    /// Gets icons filtered by search text.
    /// </summary>
    IReadOnlyList<IconInfo> SearchIcons(string searchText, int maxResults = 50);

    /// <summary>
    /// Gets the file path for an icon by name.
    /// </summary>
    string? GetIconPath(string iconName);

    /// <summary>
    /// Refreshes the icon cache by rescanning directories.
    /// </summary>
    void Refresh();
}

/// <summary>
/// Service for discovering and providing access to icon images from TOR_Armory.
/// </summary>
public class IconService : IIconService
{
    private readonly List<IconInfo> _icons = new();
    private readonly Dictionary<string, IconInfo> _iconsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _iconDirectories;

    // Icon folder configurations: (folder name, category display name)
    private static readonly (string Folder, string Category)[] IconFolders = new[]
    {
        ("ui_hud", "Traits"),
        ("ui_abilityicons", "Abilities"),
        ("ui_careersystem", "Career"),
        ("ui_TorCharacterDevelopment", "Character"),
        ("ui_TorEncyclopedia", "Encyclopedia"),
    };

    public IconService(string armoryGuiPath)
    {
        var spritesPath = Path.Combine(armoryGuiPath, "SpriteParts");
        _iconDirectories = IconFolders
            .Select(f => Path.Combine(spritesPath, f.Folder))
            .Where(Directory.Exists)
            .ToArray();

        Console.WriteLine($"[IconService] Scanning for icons in {_iconDirectories.Length} directories");
        LoadIcons(spritesPath);
    }

    private void LoadIcons(string spritesPath)
    {
        _icons.Clear();
        _iconsByName.Clear();

        foreach (var (folder, category) in IconFolders)
        {
            var folderPath = Path.Combine(spritesPath, folder);
            if (!Directory.Exists(folderPath)) continue;

            foreach (var file in Directory.EnumerateFiles(folderPath, "*.png"))
            {
                var iconName = Path.GetFileNameWithoutExtension(file);
                var info = new IconInfo
                {
                    Name = iconName,
                    FilePath = file,
                    Category = category
                };

                _icons.Add(info);
                _iconsByName.TryAdd(iconName, info);
            }
        }

        // Sort by name
        _icons.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"[IconService] Loaded {_icons.Count} icons");
    }

    public IReadOnlyList<IconInfo> GetAllIcons() => _icons;

    public IReadOnlyList<IconInfo> SearchIcons(string searchText, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return _icons.Take(maxResults).ToList();
        }

        return _icons
            .Where(i => i.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList();
    }

    public string? GetIconPath(string iconName)
    {
        if (string.IsNullOrEmpty(iconName)) return null;
        return _iconsByName.TryGetValue(iconName, out var info) ? info.FilePath : null;
    }

    public void Refresh()
    {
        if (_iconDirectories.Length > 0)
        {
            var spritesPath = Path.GetDirectoryName(_iconDirectories[0]) ?? "";
            LoadIcons(spritesPath);
        }
    }
}
