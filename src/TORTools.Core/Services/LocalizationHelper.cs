using System.Text.RegularExpressions;

namespace TORTools.Core.Services;

/// <summary>
/// Helper for handling TOR's localization string format: {=key}display text
/// </summary>
public static partial class LocalizationHelper
{
    // Pattern matches: {=some_key}Display Text (anchored to start)
    [GeneratedRegex(@"^\{=([^}]+)\}(.*)$", RegexOptions.Singleline)]
    private static partial Regex LocalizationPattern();

    // Pattern to find ALL {=key} occurrences anywhere in the string
    [GeneratedRegex(@"\{=([^}]+)\}", RegexOptions.Singleline)]
    private static partial Regex AllKeysPattern();

    /// <summary>
    /// Values that are considered null/empty in TOR XML.
    /// </summary>
    private static readonly HashSet<string> NullValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "-",
        "none",
        ""
    };

    /// <summary>
    /// Unwraps a localized string to extract the key and display text.
    /// </summary>
    /// <param name="value">The raw value from XML (e.g., "{=str_test}Display Text")</param>
    /// <returns>Tuple of (localizationKey, displayText). Key is null if not localized.</returns>
    public static (string? key, string text) Unwrap(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return (null, value ?? "");

        var match = LocalizationPattern().Match(value);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        return (null, value);
    }

    /// <summary>
    /// Wraps display text with a localization key.
    /// </summary>
    /// <param name="key">The localization key (e.g., "str_test"). Null means no wrapping.</param>
    /// <param name="displayText">The display text to wrap.</param>
    /// <returns>Wrapped string if key is provided, otherwise just the display text.</returns>
    public static string Wrap(string? key, string displayText)
    {
        if (string.IsNullOrEmpty(key))
            return displayText;

        return $"{{={key}}}{displayText}";
    }

    /// <summary>
    /// Generates a default localization key from an ID.
    /// </summary>
    /// <param name="id">The item/entry ID.</param>
    /// <param name="isAbilityTemplate">If true, uses the ability pattern: {lowercase_id}_label_str</param>
    /// <returns>A localization key like "str_[id]" or "[lowercase_id]_label_str" for abilities.</returns>
    public static string GenerateKey(string id, bool isAbilityTemplate = false)
    {
        if (isAbilityTemplate)
        {
            // Abilities use pattern: {lowercase_id}_label_str
            return $"{id.ToLowerInvariant()}_label_str";
        }
        return $"str_{id}";
    }

    /// <summary>
    /// Updates the localization key in a raw value string, preserving the display text.
    /// </summary>
    /// <param name="rawValue">The full raw value like "{=old_key}Display Text"</param>
    /// <param name="newKey">The new localization key to use</param>
    /// <returns>Updated raw value with new key</returns>
    public static string UpdateKey(string? rawValue, string newKey)
    {
        var (_, text) = Unwrap(rawValue);
        return Wrap(newKey, text);
    }

    /// <summary>
    /// Sets a new localization key while preserving the existing display text.
    /// If no localization key exists, one is added.
    /// </summary>
    /// <param name="rawValue">Current raw value</param>
    /// <param name="id">Entry ID to generate key from</param>
    /// <param name="isAbilityTemplate">Whether this is an ability template</param>
    /// <returns>Updated raw value with generated key</returns>
    public static string EnsureKey(string? rawValue, string id, bool isAbilityTemplate = false)
    {
        var (existingKey, text) = Unwrap(rawValue);

        // If key already exists, preserve it
        if (!string.IsNullOrEmpty(existingKey))
            return rawValue!;

        // Generate new key
        var newKey = GenerateKey(id, isAbilityTemplate);
        return Wrap(newKey, text);
    }

    /// <summary>
    /// Resets to a default key, overwriting any existing key.
    /// </summary>
    public static string ResetToDefaultKey(string? rawValue, string id, bool isAbilityTemplate = false)
    {
        var (_, text) = Unwrap(rawValue);
        var newKey = GenerateKey(id, isAbilityTemplate);
        return Wrap(newKey, text);
    }

    /// <summary>
    /// Checks if a value represents null/empty in TOR XML conventions.
    /// </summary>
    public static bool IsNullValue(string? value)
    {
        if (value == null)
            return true;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        return NullValues.Contains(value.Trim());
    }

    /// <summary>
    /// Normalizes a value by returning null if it's a TOR null value.
    /// </summary>
    public static string? NormalizeValue(string? value)
    {
        return IsNullValue(value) ? null : value?.Trim();
    }

    /// <summary>
    /// Counts the number of localization keys in a value.
    /// </summary>
    /// <param name="value">The raw value to check</param>
    /// <returns>Number of {=key} patterns found</returns>
    public static int CountKeys(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        return AllKeysPattern().Matches(value).Count;
    }

    /// <summary>
    /// Checks if a value has multiple localization keys (invalid state).
    /// </summary>
    public static bool HasMultipleKeys(string? value)
    {
        return CountKeys(value) > 1;
    }

    /// <summary>
    /// Checks if a value has a localization key.
    /// </summary>
    public static bool HasKey(string? value)
    {
        return CountKeys(value) > 0;
    }

    /// <summary>
    /// Extracts all localization keys from a value.
    /// </summary>
    /// <param name="value">The raw value to check</param>
    /// <returns>List of all keys found</returns>
    public static List<string> ExtractAllKeys(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return new List<string>();

        return AllKeysPattern().Matches(value)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    /// <summary>
    /// Cleans a value by removing all localization keys except the first one.
    /// If there are multiple keys, keeps only the first and removes others from the display text.
    /// </summary>
    /// <param name="value">The raw value to clean</param>
    /// <returns>Cleaned value with only one key (or no key if none existed)</returns>
    public static string CleanMultipleKeys(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? "";

        var keys = ExtractAllKeys(value);
        if (keys.Count <= 1)
            return value; // Already clean

        // Extract the first key and get clean display text
        var (firstKey, remainder) = Unwrap(value);

        // The remainder might still contain {=key} patterns - strip them all
        var cleanText = AllKeysPattern().Replace(remainder, "");

        // Re-wrap with just the first key
        return Wrap(firstKey, cleanText);
    }

    /// <summary>
    /// Validates a localization key format.
    /// </summary>
    /// <param name="value">The value to validate</param>
    /// <returns>Validation result with any issues found</returns>
    public static LocalizationValidationResult Validate(string? value)
    {
        var result = new LocalizationValidationResult();

        if (string.IsNullOrEmpty(value))
        {
            result.HasKey = false;
            return result;
        }

        var keyCount = CountKeys(value);
        result.HasKey = keyCount > 0;
        result.HasMultipleKeys = keyCount > 1;

        if (result.HasMultipleKeys)
        {
            result.Issues.Add($"Multiple localization keys found ({keyCount}). Only the first will be used.");
        }

        // Check if there's an incomplete/malformed pattern like {= without closing }
        if (value.Contains("{=") && !AllKeysPattern().IsMatch(value))
        {
            result.HasMalformedKey = true;
            result.Issues.Add("Malformed localization key pattern detected (missing closing brace?).");
        }

        return result;
    }
}

/// <summary>
/// Result of localization key validation.
/// </summary>
public class LocalizationValidationResult
{
    public bool HasKey { get; set; }
    public bool HasMultipleKeys { get; set; }
    public bool HasMalformedKey { get; set; }
    public List<string> Issues { get; } = new();
    public bool IsValid => !HasMultipleKeys && !HasMalformedKey;
}
