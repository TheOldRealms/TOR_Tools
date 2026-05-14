namespace TORTools.Core.Models.Translation;

/// <summary>
/// Configuration for a translation language.
/// </summary>
public class LanguageConfig
{
    /// <summary>
    /// The language code (e.g., "DE", "FR", "SP").
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the language (e.g., "Deutsch", "Français").
    /// </summary>
    public string LanguageName { get; set; } = string.Empty;

    /// <summary>
    /// The absolute path to the language folder.
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// List of translation file paths from language_data.xml.
    /// Each path is relative to the Languages folder (e.g., "DE/TOR_Core/ModuleData/tor_strings.xml").
    /// </summary>
    public List<string> TranslationFiles { get; set; } = new();

    /// <summary>
    /// When this language was last synced/loaded.
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Whether the language_data.xml file exists in the folder.
    /// </summary>
    public bool HasLanguageData { get; set; }

    /// <summary>
    /// Gets the display string for the sidebar.
    /// </summary>
    public string DisplayName => $"{LanguageName} ({LanguageCode})";
}
