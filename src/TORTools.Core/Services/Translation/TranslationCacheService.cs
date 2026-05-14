using System.Text.Json;
using TORTools.Core.Models.Translation;

namespace TORTools.Core.Services.Translation;

/// <summary>
/// Service for caching translation changes locally in TORTools.
/// Changes are stored in data/translations/{LanguageCode}/{RelativePath}.json
/// </summary>
public class TranslationCacheService
{
    private readonly string _cacheBasePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TranslationCacheService(string torToolsPath)
    {
        _cacheBasePath = Path.Combine(torToolsPath, "data", "translations");
    }

    /// <summary>
    /// Gets the cache file path for a translation sheet.
    /// </summary>
    public string GetCachePath(string languageCode, string relativePath)
    {
        // Convert relative path like "DE/TOR_Armory/ModuleData/tor_crafting_pieces.xml"
        // to cache path like "data/translations/DE/TOR_Armory/ModuleData/tor_crafting_pieces.json"
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return Path.Combine(_cacheBasePath, languageCode, "unknown.json");

        // Skip the language code in the relative path, use the provided one
        var pathWithoutLang = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1));
        var jsonPath = Path.ChangeExtension(pathWithoutLang, ".json");

        return Path.Combine(_cacheBasePath, languageCode, jsonPath);
    }

    /// <summary>
    /// Saves translation changes to the local cache.
    /// </summary>
    public void SaveToCache(string languageCode, string relativePath, List<CachedTranslationEntry> entries)
    {
        var cachePath = GetCachePath(languageCode, relativePath);
        var cacheDir = Path.GetDirectoryName(cachePath);

        if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        var cache = new TranslationCache
        {
            LanguageCode = languageCode,
            RelativePath = relativePath,
            LastModified = DateTime.UtcNow,
            Entries = entries
        };

        var json = JsonSerializer.Serialize(cache, JsonOptions);
        File.WriteAllText(cachePath, json);

        Console.WriteLine($"[TranslationCache] Saved {entries.Count} entries to {cachePath}");
    }

    /// <summary>
    /// Loads cached translation changes if they exist.
    /// </summary>
    public TranslationCache? LoadFromCache(string languageCode, string relativePath)
    {
        var cachePath = GetCachePath(languageCode, relativePath);

        if (!File.Exists(cachePath))
        {
            Console.WriteLine($"[TranslationCache] No cache found at {cachePath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(cachePath);
            var cache = JsonSerializer.Deserialize<TranslationCache>(json, JsonOptions);
            Console.WriteLine($"[TranslationCache] Loaded {cache?.Entries.Count ?? 0} entries from cache");
            return cache;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TranslationCache] Error loading cache: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if there are cached changes for a translation sheet.
    /// </summary>
    public bool HasCachedChanges(string languageCode, string relativePath)
    {
        var cachePath = GetCachePath(languageCode, relativePath);
        return File.Exists(cachePath);
    }

    /// <summary>
    /// Clears the cache for a specific translation sheet (after export).
    /// </summary>
    public void ClearCache(string languageCode, string relativePath)
    {
        var cachePath = GetCachePath(languageCode, relativePath);

        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
            Console.WriteLine($"[TranslationCache] Cleared cache at {cachePath}");
        }
    }

    /// <summary>
    /// Gets all cached languages.
    /// </summary>
    public List<string> GetCachedLanguages()
    {
        if (!Directory.Exists(_cacheBasePath))
            return new List<string>();

        return Directory.GetDirectories(_cacheBasePath)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Gets all cached files for a language.
    /// </summary>
    public List<string> GetCachedFiles(string languageCode)
    {
        var langPath = Path.Combine(_cacheBasePath, languageCode);
        if (!Directory.Exists(langPath))
            return new List<string>();

        return Directory.GetFiles(langPath, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(langPath, f))
            .ToList();
    }
}

/// <summary>
/// Cached translation data for a single file.
/// </summary>
public class TranslationCache
{
    public string LanguageCode { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public DateTime LastModified { get; set; }
    public List<CachedTranslationEntry> Entries { get; set; } = new();
}

/// <summary>
/// A single cached translation entry.
/// </summary>
public class CachedTranslationEntry
{
    public string LocalizationId { get; set; } = "";
    public string? TranslatedText { get; set; }
    public bool IsRemoved { get; set; }  // For tracking removed orphaned entries
}
