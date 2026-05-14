namespace TORTools.Core.Models.Translation;

/// <summary>
/// Represents a translation sheet for a single XML file.
/// Contains all translatable entries merged with their translations.
/// </summary>
public class TranslationSheet
{
    /// <summary>
    /// The source XML filename (e.g., "tor_strings.xml").
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The relative path within the module structure.
    /// (e.g., "TOR_Core/ModuleData/tor_strings.xml")
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// The language code this sheet is for.
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// All translation entries for this file.
    /// </summary>
    public List<TranslationEntry> Entries { get; set; } = new();

    /// <summary>
    /// Whether any entries have been modified locally.
    /// </summary>
    public bool IsDirty => Entries.Any(e => e.IsDirty);

    /// <summary>
    /// Count of translated entries.
    /// </summary>
    public int TranslatedCount => Entries.Count(e => e.Status == TranslationStatus.Translated);

    /// <summary>
    /// Count of TODO entries.
    /// </summary>
    public int TodoCount => Entries.Count(e => e.Status == TranslationStatus.Todo);

    /// <summary>
    /// Count of missing entries.
    /// </summary>
    public int MissingCount => Entries.Count(e => e.Status == TranslationStatus.Missing);

    /// <summary>
    /// Count of orphaned entries.
    /// </summary>
    public int OrphanedCount => Entries.Count(e => e.Status == TranslationStatus.Orphaned);

    /// <summary>
    /// Total entry count (excluding orphaned).
    /// </summary>
    public int TotalSourceEntries => Entries.Count(e => e.Status != TranslationStatus.Orphaned);

    /// <summary>
    /// Completion percentage (translated / total source entries).
    /// </summary>
    public double CompletionPercent => TotalSourceEntries > 0
        ? (double)TranslatedCount / TotalSourceEntries * 100
        : 0;

    /// <summary>
    /// Gets a summary string for display.
    /// </summary>
    public string Summary => $"{TranslatedCount}/{TotalSourceEntries} ({CompletionPercent:F0}%)";
}
