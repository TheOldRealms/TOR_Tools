namespace TORTools.Core.Models.Translation;

/// <summary>
/// Represents a single translatable string entry.
/// </summary>
public class TranslationEntry
{
    /// <summary>
    /// The localization ID (e.g., "str_tor_ability_fireball").
    /// </summary>
    public string LocalizationId { get; set; } = string.Empty;

    /// <summary>
    /// The original English text from the source XML.
    /// </summary>
    public string EnglishText { get; set; } = string.Empty;

    /// <summary>
    /// The translated text, or null if not translated.
    /// For TODO entries, this contains "TODO [English text]".
    /// </summary>
    public string? TranslatedText { get; set; }

    /// <summary>
    /// The translation status.
    /// </summary>
    public TranslationStatus Status { get; set; }

    /// <summary>
    /// The source XML file this entry came from (e.g., "tor_abilities.xml").
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// The relative path within the translation folder structure.
    /// (e.g., "TOR_Core/ModuleData/tor_abilities.xml")
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether this entry has been modified locally and needs to be saved.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Gets the display text for the translation column.
    /// Shows the translated text, or status indicator for special cases.
    /// </summary>
    public string DisplayText => Status switch
    {
        TranslationStatus.Orphaned => TranslatedText ?? "???",
        TranslationStatus.Missing => string.Empty,
        TranslationStatus.Todo => TranslatedText ?? $"TODO [{EnglishText}]",
        _ => TranslatedText ?? string.Empty
    };

    /// <summary>
    /// Gets the English text for display, or "???" for orphaned entries.
    /// </summary>
    public string DisplayEnglish => Status == TranslationStatus.Orphaned ? "???" : EnglishText;
}
