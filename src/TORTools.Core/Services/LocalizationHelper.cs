using System.Text.RegularExpressions;

namespace TORTools.Core.Services;

/// <summary>
/// Helper for handling TOR's localization string format: {=key}display text
/// </summary>
public static partial class LocalizationHelper
{
    // Pattern matches: {=some_key}Display Text
    [GeneratedRegex(@"^\{=([^}]+)\}(.*)$", RegexOptions.Singleline)]
    private static partial Regex LocalizationPattern();

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
    /// <returns>A localization key like "str_[id]".</returns>
    public static string GenerateKey(string id)
    {
        return $"str_{id}";
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
}
