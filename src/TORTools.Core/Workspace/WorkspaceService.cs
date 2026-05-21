using System.Text.Json;
using TORTools.Core.Models;

namespace TORTools.Core.Workspace;

/// <summary>
/// Service for managing the TOR workspace configuration and file discovery.
/// </summary>
public class WorkspaceService : IWorkspaceService
{

    /// <summary>
    /// Maps XML file names to their catalog and display name.
    /// </summary>
    private static readonly Dictionary<string, (string Catalog, string DisplayName)> FileCatalogMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Item Catalog - TOR_Armory
        ["tor_armors.xml"] = ("Item Catalog", "Armors"),
        ["tor_meleeweapons.xml"] = ("Item Catalog", "Melee Weapons"),
        ["tor_rangedweapons.xml"] = ("Item Catalog", "Ranged Weapons"),
        ["tor_shields.xml"] = ("Item Catalog", "Shields and Offhand"),
        ["tor_projectiles.xml"] = ("Item Catalog", "Projectiles"),
        ["tor_other_items.xml"] = ("Item Catalog", "Other Items"),
        ["tor_horseandharness.xml"] = ("Item Catalog", "Horses & Harness"),

        // Item Catalog - TOR_Core (item extensions)
        ["tor_itemtraits.xml"] = ("Item Catalog", "Item Traits"),
        // tor_extendeditemproperties.xml is accessed via cross-references, not directly edited

        // Unit Catalog - TOR_Core
        // tor_heroes.xml is merged into Campaign Lords, not edited directly
        ["tor_campaign_lords.xml"] = ("Unit Catalog", "Campaign Lords"),
        ["tor_troopdefinitions.xml"] = ("Unit Catalog", "Troop Definitions"),
        ["tor_charactertemplates.xml"] = ("Unit Catalog", "Character Templates"),
        // ["tor_dummyNPCs.xml"] = ("Unit Catalog", "Dummy NPCs"), // Tournament templates - not editable
        // tor_extendedunitproperties.xml - data accessed via cross-references in Troops table (Attributes, Abilities, etc.)
        ["tor_bodyproperties.xml"] = ("Unit Catalog", "Body Properties"),

        // Equipment Sets (part of Unit Catalog)
        ["tor_equipment_sets.xml"] = ("Unit Catalog", "Equipment Sets"),

        // Abilities & Effects Catalog
        ["tor_abilitytemplates.xml"] = ("Abilities & Effects", "Ability Templates"),
        ["tor_statuseffects.xml"] = ("Abilities & Effects", "Status Effects"),
        ["tor_triggeredeffects.xml"] = ("Abilities & Effects", "Triggered Effects"),
        ["tor_attributes.xml"] = ("Abilities & Effects", "Unit Attributes"),

        // Factions Catalog
        ["tor_clans.xml"] = ("Factions", "Clans"),
        ["tor_cultures.xml"] = ("Factions", "Cultures"),
        ["tor_kingdoms.xml"] = ("Factions", "Kingdoms"),

        // Crafting Catalog
        ["tor_crafting_pieces.xml"] = ("Crafting", "Crafting Pieces"),
        ["tor_crafting_templates.xml"] = ("Crafting", "Crafting Templates"),

        // Settlements Catalog
        ["tor_settlements.xml"] = ("Settlements", "TOR Settlements"),
        ["settlements.xml"] = ("Settlements", "Settlements"),

        // Configuration
        ["tor_config.xml"] = ("Configuration", "Config"),
        ["tor_cc_options.xml"] = ("Configuration", "Character Creation Options"),
        ["tor_skillsets.xml"] = ("Unit Catalog", "Skill Sets"),
        ["tor_specialization_options.xml"] = ("Configuration", "Specialization Options"),
        ["tor_races.xml"] = ("Configuration", "Races"),

        // Armory Metadata
        ["tor_monsters.xml"] = ("Creatures", "Monsters"),
        ["tor_monster_usage_sets.xml"] = ("Creatures", "Monster Usage Sets"),
        ["tor_action_sets.xml"] = ("Animation", "Action Sets"),
        ["tor_voice_definitions.xml"] = ("Animation", "Voice Definitions"),
        ["tor_weapon_descriptions.xml"] = ("Item Catalog", "Weapon Descriptions"),

        // Text Catalog - Localization
        ["tor_strings.xml"] = ("Text Catalog", "Strings / Localization"),
        ["tor_string_overrides.xml"] = ("Text Catalog", "String Overrides (Vanilla)"),
        ["tor_voiced_strings.xml"] = ("Text Catalog", "Voiced Strings"),
        ["tor_tags.xml"] = ("Text Catalog", "String Tags"),
    };

    /// <summary>
    /// Defines the display order of catalogs.
    /// </summary>
    private static readonly string[] CatalogOrder =
    [
        "Item Catalog",
        "Unit Catalog",
        "Abilities & Effects",
        "Text Catalog",
        "Factions",
        "Crafting",
        "Settlements",
        "Creatures",
        "Animation",
        "Configuration",
        "Other"
    ];

    /// <summary>
    /// Defines the display order of files within each catalog.
    /// Lower numbers appear first. Files not in this list get order 100.
    /// </summary>
    private static readonly Dictionary<string, int> FileOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        // Item Catalog - main item types first
        ["tor_armors.xml"] = 1,
        ["tor_meleeweapons.xml"] = 2,
        ["tor_rangedweapons.xml"] = 3,
        ["tor_shields.xml"] = 4,
        ["tor_projectiles.xml"] = 10,
        ["tor_other_items.xml"] = 11,
        ["tor_horseandharness.xml"] = 12,
        ["tor_itemtraits.xml"] = 20,
        ["tor_weapon_descriptions.xml"] = 21,

        // Unit Catalog - troops and their equipment
        ["tor_troopdefinitions.xml"] = 1,
        ["tor_equipment_sets.xml"] = 2,
        ["tor_skillsets.xml"] = 3,
        // tor_heroes.xml removed - merged into Campaign Lords
        ["tor_campaign_lords.xml"] = 10,
        ["tor_charactertemplates.xml"] = 12,
        ["tor_bodyproperties.xml"] = 20,
        ["tor_extendedunitproperties.xml"] = 21,
        // ["tor_dummyNPCs.xml"] = 30, // Tournament templates - not editable

        // Factions Catalog - kingdoms, clans, cultures
        ["tor_kingdoms.xml"] = 1,
        ["tor_clans.xml"] = 2,
        ["tor_cultures.xml"] = 3,
    };

    public string ConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TORTools",
        "workspace.json");

    public WorkspaceConfig AutoDetect()
    {
        var config = new WorkspaceConfig();

        // Detect Bannerlord path by walking up from current directory
        // The app is expected to run from within Modules/TORTools
        var currentDir = Directory.GetCurrentDirectory();
        config.BannerlordPath = FindBannerlordPath(currentDir);

        if (config.BannerlordPath != null)
        {
            var modulesPath = Path.Combine(config.BannerlordPath, "Modules");

            // Check for TOR repositories (sibling modules)
            var torCorePath = Path.Combine(modulesPath, "TOR_Core");
            if (Directory.Exists(torCorePath))
                config.TorCorePath = torCorePath;

            var torArmoryPath = Path.Combine(modulesPath, "TOR_Armory");
            if (Directory.Exists(torArmoryPath))
                config.TorArmoryPath = torArmoryPath;

            var torEnvironmentPath = Path.Combine(modulesPath, "TOR_Environment");
            if (Directory.Exists(torEnvironmentPath))
                config.TorEnvironmentPath = torEnvironmentPath;
        }

        return config;
    }

    /// <summary>
    /// Walks up from the given directory to find the Bannerlord installation root.
    /// Looks for a parent directory that contains a "Modules" folder.
    /// </summary>
    private static string? FindBannerlordPath(string startDir)
    {
        var dir = new DirectoryInfo(startDir);

        while (dir != null)
        {
            // Check if this directory has a Modules subfolder
            var modulesPath = Path.Combine(dir.FullName, "Modules");
            if (Directory.Exists(modulesPath))
            {
                // Verify it looks like Bannerlord (has bin folder or Bannerlord.exe)
                var hasBin = Directory.Exists(Path.Combine(dir.FullName, "bin"));
                var hasExe = File.Exists(Path.Combine(dir.FullName, "bin", "Win64_Shipping_Client", "Bannerlord.exe")) ||
                             File.Exists(Path.Combine(dir.FullName, "bin", "Win64_Shipping_Client", "Bannerlord.Native.exe"));

                if (hasBin || hasExe)
                {
                    return dir.FullName;
                }

                // If we're inside the Modules folder itself, go up one more
                if (dir.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase))
                {
                    return dir.Parent?.FullName;
                }
            }

            // Check if we ARE the Modules folder
            if (dir.Name.Equals("Modules", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
            {
                return dir.Parent.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public WorkspaceConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<WorkspaceConfig>(json);
                if (config != null)
                    return config;
            }
        }
        catch
        {
            // If loading fails, return auto-detected config
        }

        return AutoDetect();
    }

    public void SaveConfig(WorkspaceConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigFilePath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigFilePath, json);
    }

    public IReadOnlyList<XmlFileInfo> GetXmlFiles(WorkspaceConfig config)
    {
        var files = new List<XmlFileInfo>();

        // First scan data folder relative to app location (works regardless of folder name)
        var toolDataFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var torToolsDataPath = Services.FilePathResolver.GetDataDirectory();
        if (torToolsDataPath != null)
        {
            foreach (var file in ScanToolDataFolder(torToolsDataPath))
            {
                files.Add(file);
                toolDataFiles.Add(file.FileName);
            }
        }

        // Then scan other repositories, skipping files that exist in TORTools/data
        if (!string.IsNullOrEmpty(config.TorCorePath))
            files.AddRange(ScanRepository(config.TorCorePath, "TOR_Core")
                .Where(f => !toolDataFiles.Contains(f.FileName)));

        if (!string.IsNullOrEmpty(config.TorArmoryPath))
            files.AddRange(ScanRepository(config.TorArmoryPath, "TOR_Armory")
                .Where(f => !toolDataFiles.Contains(f.FileName)));

        if (!string.IsNullOrEmpty(config.TorEnvironmentPath))
            files.AddRange(ScanRepository(config.TorEnvironmentPath, "TOR_Environment")
                .Where(f => !toolDataFiles.Contains(f.FileName)));

        return files;
    }

    private IEnumerable<XmlFileInfo> ScanToolDataFolder(string dataPath)
    {
        if (!Directory.Exists(dataPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(dataPath, "*.xml"))
        {
            var fileName = Path.GetFileName(file);
            // Only include files that have a catalog mapping
            if (FileCatalogMap.TryGetValue(fileName, out var catalogInfo))
            {
                var fileInfo = new FileInfo(file);
                yield return new XmlFileInfo
                {
                    FilePath = file,
                    DisplayName = catalogInfo.DisplayName,
                    Category = catalogInfo.Catalog,
                    Repository = "TORTools",
                    RelativePath = Path.Combine("data", fileName),
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                };
            }
        }
    }

    /// <summary>
    /// Gets XML files organized by catalog (spanning across repositories).
    /// </summary>
    public IReadOnlyList<CatalogGroup> GetCatalogs(WorkspaceConfig config)
    {
        var allFiles = GetXmlFiles(config);

        var catalogs = allFiles
            .GroupBy(f => GetCatalogInfo(f.FileName).Catalog)
            .Select(g => new CatalogGroup
            {
                Name = g.Key,
                Files = g.OrderBy(f => FileOrder.TryGetValue(f.FileName, out var order) ? order : 100)
                         .ThenBy(f => GetCatalogInfo(f.FileName).DisplayName).ToList()
            })
            .OrderBy(c => Array.IndexOf(CatalogOrder, c.Name) is var idx && idx >= 0 ? idx : 999)
            .ToList();

        return catalogs;
    }

    private IEnumerable<XmlFileInfo> ScanRepository(string repoPath, string repoName)
    {
        var moduleDataPath = Path.Combine(repoPath, "ModuleData");
        if (!Directory.Exists(moduleDataPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(moduleDataPath, "*.xml", SearchOption.AllDirectories))
        {
            // Skip tor_skins.xml (excluded per requirements - 6.6MB, needs rework)
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("tor_skins.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip Language folders for now
            if (file.Contains("Languages", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileInfo = new FileInfo(file);
            var relativePath = Path.GetRelativePath(repoPath, file);
            var catalogInfo = GetCatalogInfo(fileName);

            yield return new XmlFileInfo
            {
                FilePath = file,
                Category = catalogInfo.Catalog,
                DisplayName = catalogInfo.DisplayName,
                Repository = repoName,
                RelativePath = relativePath,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime
            };
        }
    }

    private static (string Catalog, string DisplayName) GetCatalogInfo(string fileName)
    {
        if (FileCatalogMap.TryGetValue(fileName, out var info))
            return info;

        // Default: use file name without extension
        var displayName = Path.GetFileNameWithoutExtension(fileName);

        // Determine catalog based on file name patterns
        if (fileName.StartsWith("tor_", StringComparison.OrdinalIgnoreCase))
            return ("Other", displayName);

        return ("Vanilla", displayName);
    }

    public WorkspaceValidationResult ValidateWorkspace(WorkspaceConfig config)
    {
        var result = new WorkspaceValidationResult
        {
            TorCoreFound = !string.IsNullOrEmpty(config.TorCorePath) && Directory.Exists(config.TorCorePath),
            TorArmoryFound = !string.IsNullOrEmpty(config.TorArmoryPath) && Directory.Exists(config.TorArmoryPath),
            TorEnvironmentFound = !string.IsNullOrEmpty(config.TorEnvironmentPath) && Directory.Exists(config.TorEnvironmentPath),
            Errors = new List<string>(),
            Warnings = new List<string>()
        };

        if (!result.TorCoreFound && !result.TorArmoryFound && !result.TorEnvironmentFound)
        {
            result.Errors.Add("No TOR repositories found. Please configure workspace paths.");
        }

        if (!result.TorCoreFound && !string.IsNullOrEmpty(config.TorCorePath))
        {
            result.Warnings.Add($"TOR_Core path not found: {config.TorCorePath}");
        }

        if (!result.TorArmoryFound && !string.IsNullOrEmpty(config.TorArmoryPath))
        {
            result.Warnings.Add($"TOR_Armory path not found: {config.TorArmoryPath}");
        }

        if (!result.TorEnvironmentFound && !string.IsNullOrEmpty(config.TorEnvironmentPath))
        {
            result.Warnings.Add($"TOR_Environment path not found: {config.TorEnvironmentPath}");
        }

        result = result with
        {
            IsValid = result.Errors.Count == 0 && (result.TorCoreFound || result.TorArmoryFound)
        };

        return result;
    }
}

/// <summary>
/// A group of XML files within a catalog.
/// </summary>
public class CatalogGroup
{
    public required string Name { get; init; }
    public required List<XmlFileInfo> Files { get; init; }
}
