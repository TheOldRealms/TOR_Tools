namespace TORTools.Core.Models.Translation;

/// <summary>
/// Status of a translation entry.
/// </summary>
public enum TranslationStatus
{
    /// <summary>
    /// Entry has a complete translation.
    /// </summary>
    Translated,

    /// <summary>
    /// Entry is marked as TODO (needs translation).
    /// </summary>
    Todo,

    /// <summary>
    /// Entry exists in English source but not in translation file.
    /// </summary>
    Missing,

    /// <summary>
    /// Entry exists in translation file but not in English source (orphaned).
    /// </summary>
    Orphaned
}
