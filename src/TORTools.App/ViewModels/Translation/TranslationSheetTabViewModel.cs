using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _filterCts;
    private const int FilterDebounceMs = 300;

    public TranslationSheetTabViewModel(
        TranslationSheet sheet,
        TranslationService translationService,
        LanguageConfig languageConfig)
    {
        _sheet = sheet;
        _translationService = translationService;
        _languageConfig = languageConfig;

        // Create row ViewModels
        foreach (var entry in sheet.Entries)
        {
            var row = new TranslationEntryRowViewModel(entry);
            row.Initialize();
            _allRows.Add(row);
        }

        // Initially show all rows
        foreach (var row in _allRows)
        {
            Rows.Add(row);
        }

        UpdateStats();
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
        // Build output path
        var outputPath = Path.Combine(
            _languageConfig.FolderPath,
            RelativePath.Replace('/', Path.DirectorySeparatorChar));

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

        HasUnsavedChanges = false;
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

        // Remove from both collections
        _allRows.Remove(row);
        Rows.Remove(row);

        // Also remove from the underlying sheet
        _sheet.Entries.Remove(row.Entry);

        // Mark as having unsaved changes and update stats
        HasUnsavedChanges = true;
        UpdateStats();
    }

    private bool CanRemoveOrphanedEntry(TranslationEntryRowViewModel? row)
    {
        return row != null && row.Status == TranslationStatus.Orphaned;
    }

    public void Dispose()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
    }
}
