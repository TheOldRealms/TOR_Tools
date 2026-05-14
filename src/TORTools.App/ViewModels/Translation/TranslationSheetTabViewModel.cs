using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Models.Translation;
using TORTools.Core.Services.Translation;

namespace TORTools.App.ViewModels.Translation;

/// <summary>
/// ViewModel for a translation sheet tab.
/// </summary>
public partial class TranslationSheetTabViewModel : ViewModelBase, IDisposable
{
    private readonly TranslationSheet _sheet;
    private readonly TranslationService _translationService;
    private readonly LanguageConfig _languageConfig;
    private readonly TranslationCacheService? _cacheService;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _saveCts;
    private const int FilterDebounceMs = 300;
    private const int SaveDebounceMs = 1000;
    private readonly HashSet<string> _removedEntryIds = new();

    public TranslationSheetTabViewModel(
        TranslationSheet sheet,
        TranslationService translationService,
        LanguageConfig languageConfig,
        TranslationCacheService? cacheService = null)
    {
        _sheet = sheet;
        _translationService = translationService;
        _languageConfig = languageConfig;
        _cacheService = cacheService;

        // Create row ViewModels
        foreach (var entry in sheet.Entries)
        {
            var row = new TranslationEntryRowViewModel(entry);
            row.Initialize();
            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
        }

        // Load cached changes and apply them
        LoadCachedChanges();

        // Initially show all rows
        foreach (var row in _allRows)
        {
            Rows.Add(row);
        }

        UpdateStats();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationEntryRowViewModel.TranslatedText) ||
            e.PropertyName == nameof(TranslationEntryRowViewModel.IsDirty))
        {
            UpdateStats();
            SaveToCacheDebounced();
        }
    }

    private void LoadCachedChanges()
    {
        if (_cacheService == null) return;

        var cache = _cacheService.LoadFromCache(LanguageCode, RelativePath);
        if (cache == null) return;

        Console.WriteLine($"[TranslationSheet] Applying {cache.Entries.Count} cached changes");

        // Build lookup for fast access
        var rowLookup = _allRows.ToDictionary(r => r.LocalizationId);

        foreach (var cachedEntry in cache.Entries)
        {
            if (cachedEntry.IsRemoved)
            {
                // Entry was removed - track it and remove from rows
                _removedEntryIds.Add(cachedEntry.LocalizationId);
                if (rowLookup.TryGetValue(cachedEntry.LocalizationId, out var rowToRemove))
                {
                    rowToRemove.PropertyChanged -= OnRowPropertyChanged;
                    _allRows.Remove(rowToRemove);
                    _sheet.Entries.Remove(rowToRemove.Entry);
                }
            }
            else if (rowLookup.TryGetValue(cachedEntry.LocalizationId, out var row))
            {
                // Apply cached translation
                if (cachedEntry.TranslatedText != null && cachedEntry.TranslatedText != row.TranslatedText)
                {
                    row.TranslatedText = cachedEntry.TranslatedText;
                    row.IsDirty = true;
                }
            }
        }

        // Mark as having cached changes if any were applied
        HasUnsavedChanges = _allRows.Any(r => r.IsDirty) || _removedEntryIds.Count > 0;
    }

    private void SaveToCacheDebounced()
    {
        if (_cacheService == null) return;

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();

        var token = _saveCts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounceMs, token);
                if (!token.IsCancellationRequested)
                {
                    SaveToCache();
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored
            }
        });
    }

    private void SaveToCache()
    {
        if (_cacheService == null) return;

        var entries = new List<CachedTranslationEntry>();

        // Add dirty rows
        foreach (var row in _allRows.Where(r => r.IsDirty))
        {
            entries.Add(new CachedTranslationEntry
            {
                LocalizationId = row.LocalizationId,
                TranslatedText = row.TranslatedText,
                IsRemoved = false
            });
        }

        // Add removed entries
        foreach (var removedId in _removedEntryIds)
        {
            entries.Add(new CachedTranslationEntry
            {
                LocalizationId = removedId,
                TranslatedText = null,
                IsRemoved = true
            });
        }

        if (entries.Count > 0)
        {
            _cacheService.SaveToCache(LanguageCode, RelativePath, entries);
        }
    }

    /// <summary>
    /// Tab title for display.
    /// </summary>
    public string Title => $"{_languageConfig.LanguageCode}:{_sheet.FileName}";

    /// <summary>
    /// Full title with language name.
    /// </summary>
    public string FullTitle => $"{_languageConfig.LanguageName} - {_sheet.FileName}";

    /// <summary>
    /// The language code.
    /// </summary>
    public string LanguageCode => _languageConfig.LanguageCode;

    /// <summary>
    /// The source file name.
    /// </summary>
    public string FileName => _sheet.FileName;

    /// <summary>
    /// The relative path for export.
    /// </summary>
    public string RelativePath => _sheet.RelativePath;

    /// <summary>
    /// All rows (unfiltered).
    /// </summary>
    private readonly List<TranslationEntryRowViewModel> _allRows = new();

    /// <summary>
    /// Displayed rows (may be filtered).
    /// </summary>
    public ObservableCollection<TranslationEntryRowViewModel> Rows { get; } = new();

    /// <summary>
    /// Whether there are unsaved changes.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Filter text for searching entries.
    /// </summary>
    [ObservableProperty]
    private string _filterText = "";

    /// <summary>
    /// Status filter (null = show all).
    /// </summary>
    [ObservableProperty]
    private TranslationStatus? _statusFilter;

    /// <summary>
    /// Whether a filter operation is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isFiltering;

    /// <summary>
    /// Completion percentage display.
    /// </summary>
    [ObservableProperty]
    private string _completionText = "0%";

    /// <summary>
    /// Translated count.
    /// </summary>
    [ObservableProperty]
    private int _translatedCount;

    /// <summary>
    /// TODO count.
    /// </summary>
    [ObservableProperty]
    private int _todoCount;

    /// <summary>
    /// Missing count.
    /// </summary>
    [ObservableProperty]
    private int _missingCount;

    /// <summary>
    /// Orphaned count.
    /// </summary>
    [ObservableProperty]
    private int _orphanedCount;

    /// <summary>
    /// Total entries (excluding orphaned).
    /// </summary>
    [ObservableProperty]
    private int _totalEntries;

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilterDebounced();
    }

    partial void OnStatusFilterChanged(TranslationStatus? value)
    {
        ApplyFilterDebounced();
    }

    private void ApplyFilterDebounced()
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();

        var token = _filterCts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FilterDebounceMs, token);
                if (!token.IsCancellationRequested)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ApplyFilter();
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored
            }
        });
    }

    private void ApplyFilter()
    {
        IsFiltering = true;

        Rows.Clear();

        foreach (var row in _allRows)
        {
            // Status filter
            if (StatusFilter.HasValue && row.Status != StatusFilter.Value)
                continue;

            // Text filter
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var filter = FilterText.Trim();
                if (!row.LocalizationId.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !row.EnglishText.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !(row.TranslatedText?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }
            }

            Rows.Add(row);
        }

        IsFiltering = false;
    }

    /// <summary>
    /// Updates statistics.
    /// </summary>
    public void UpdateStats()
    {
        TranslatedCount = _allRows.Count(r => r.Status == TranslationStatus.Translated);
        TodoCount = _allRows.Count(r => r.Status == TranslationStatus.Todo);
        MissingCount = _allRows.Count(r => r.Status == TranslationStatus.Missing);
        OrphanedCount = _allRows.Count(r => r.Status == TranslationStatus.Orphaned);
        TotalEntries = _allRows.Count(r => r.Status != TranslationStatus.Orphaned);

        var percent = TotalEntries > 0 ? (double)TranslatedCount / TotalEntries * 100 : 0;
        CompletionText = $"{percent:F0}%";

        HasUnsavedChanges = _allRows.Any(r => r.IsDirty);
    }

    /// <summary>
    /// Exports the translation sheet to the target folder.
    /// </summary>
    [RelayCommand]
    private void Export()
    {
        // Build output path - strip the language code from RelativePath since FolderPath already includes it
        // RelativePath is like "DE/TOR_Armory/ModuleData/file.xml", we need "TOR_Armory/ModuleData/file.xml"
        var parts = RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathWithoutLang = parts.Length > 1
            ? string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1))
            : RelativePath;

        var outputPath = Path.Combine(_languageConfig.FolderPath, pathWithoutLang);

        // Sync row changes back to sheet
        foreach (var row in _allRows)
        {
            row.Entry.TranslatedText = row.TranslatedText;
        }

        // Export
        _translationService.ExportTranslationSheet(_sheet, outputPath, _languageConfig.LanguageName);

        // Mark all as saved
        foreach (var row in _allRows)
        {
            row.IsDirty = false;
            row.Entry.IsDirty = false;
        }

        // Clear the cache since changes are now exported
        _cacheService?.ClearCache(LanguageCode, RelativePath);
        _removedEntryIds.Clear();

        HasUnsavedChanges = false;
        Console.WriteLine($"[TranslationSheet] Exported to {outputPath} and cleared cache");
    }

    /// <summary>
    /// Clears the status filter (show all).
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = "";
        StatusFilter = null;
    }

    /// <summary>
    /// Shows only TODO entries.
    /// </summary>
    [RelayCommand]
    private void ShowTodoOnly()
    {
        StatusFilter = TranslationStatus.Todo;
    }

    /// <summary>
    /// Shows only missing entries.
    /// </summary>
    [RelayCommand]
    private void ShowMissingOnly()
    {
        StatusFilter = TranslationStatus.Missing;
    }

    /// <summary>
    /// Shows only orphaned entries.
    /// </summary>
    [RelayCommand]
    private void ShowOrphanedOnly()
    {
        StatusFilter = TranslationStatus.Orphaned;
    }

    /// <summary>
    /// Removes an orphaned entry from the sheet.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveOrphanedEntry))]
    private void RemoveOrphanedEntry(TranslationEntryRowViewModel? row)
    {
        if (row == null || row.Status != TranslationStatus.Orphaned)
            return;

        // Track the removed entry for cache
        _removedEntryIds.Add(row.LocalizationId);

        // Unsubscribe from events
        row.PropertyChanged -= OnRowPropertyChanged;

        // Remove from both collections
        _allRows.Remove(row);
        Rows.Remove(row);

        // Also remove from the underlying sheet
        _sheet.Entries.Remove(row.Entry);

        // Mark as having unsaved changes and update stats
        HasUnsavedChanges = true;
        UpdateStats();

        // Save to cache
        SaveToCacheDebounced();
    }

    private bool CanRemoveOrphanedEntry(TranslationEntryRowViewModel? row)
    {
        return row != null && row.Status == TranslationStatus.Orphaned;
    }

    public void Dispose()
    {
        // Unsubscribe from all row events
        foreach (var row in _allRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _saveCts?.Cancel();
        _saveCts?.Dispose();
    }
}
