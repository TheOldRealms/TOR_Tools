using CommunityToolkit.Mvvm.ComponentModel;
using TORTools.Core.Models.Translation;

namespace TORTools.App.ViewModels.Translation;

/// <summary>
/// ViewModel for a single translation entry row in the grid.
/// </summary>
public partial class TranslationEntryRowViewModel : ViewModelBase
{
    private readonly TranslationEntry _entry;

    public TranslationEntryRowViewModel(TranslationEntry entry)
    {
        _entry = entry;
    }

    /// <summary>
    /// The underlying translation entry.
    /// </summary>
    public TranslationEntry Entry => _entry;

    /// <summary>
    /// The localization ID.
    /// </summary>
    public string LocalizationId => _entry.LocalizationId;

    /// <summary>
    /// The English source text (or "???" for orphaned entries).
    /// </summary>
    public string EnglishText => _entry.DisplayEnglish;

    /// <summary>
    /// The translated text.
    /// </summary>
    [ObservableProperty]
    private string _translatedText = "";

    /// <summary>
    /// The translation status.
    /// </summary>
    public TranslationStatus Status => _entry.Status;

    /// <summary>
    /// Status indicator for display.
    /// </summary>
    public string StatusIndicator => Status switch
    {
        TranslationStatus.Translated => "OK",
        TranslationStatus.Todo => "TODO",
        TranslationStatus.Missing => "MISS",
        TranslationStatus.Orphaned => "???",
        _ => "?"
    };

    /// <summary>
    /// Whether this is an orphaned entry (for context menu binding).
    /// </summary>
    public bool IsOrphaned => Status == TranslationStatus.Orphaned;

    /// <summary>
    /// Whether the row has been modified.
    /// </summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// The source file this entry came from.
    /// </summary>
    public string SourceFile => _entry.SourceFile;

    /// <summary>
    /// Whether this row is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Constructor initialization.
    /// </summary>
    partial void OnTranslatedTextChanged(string value)
    {
        if (value != _entry.TranslatedText)
        {
            _entry.TranslatedText = value;
            _entry.IsDirty = true;
            IsDirty = true;

            // Update status based on new text
            UpdateStatus();
        }
    }

    /// <summary>
    /// Updates the entry status based on current translation text.
    /// </summary>
    private void UpdateStatus()
    {
        if (string.IsNullOrEmpty(TranslatedText))
        {
            _entry.Status = TranslationStatus.Missing;
        }
        else if (TranslatedText.StartsWith("TODO", StringComparison.OrdinalIgnoreCase))
        {
            _entry.Status = TranslationStatus.Todo;
        }
        else if (_entry.Status != TranslationStatus.Orphaned)
        {
            _entry.Status = TranslationStatus.Translated;
        }

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusIndicator));
    }

    /// <summary>
    /// Initialize the translated text from the entry (call after construction).
    /// </summary>
    public void Initialize()
    {
        TranslatedText = _entry.TranslatedText ?? "";
    }
}
