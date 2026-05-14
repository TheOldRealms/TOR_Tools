using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Models.Translation;
using TORTools.Core.Services.Translation;

namespace TORTools.App.ViewModels.Translation;

/// <summary>
/// ViewModel for the Translations sidebar section.
/// Manages loaded languages and their translation files.
/// </summary>
public partial class TranslationsSidebarViewModel : ViewModelBase
{
    private readonly string _modulesBasePath;
    private readonly TranslationService _translationService;
    private readonly LanguageDataGenerator _languageDataGenerator;

    /// <summary>
    /// Event raised when user wants to open a translation sheet.
    /// </summary>
    public event EventHandler<OpenTranslationSheetEventArgs>? OpenTranslationSheetRequested;

    public TranslationsSidebarViewModel(string modulesBasePath)
    {
        _modulesBasePath = modulesBasePath;
        _translationService = new TranslationService(modulesBasePath);
        _languageDataGenerator = new LanguageDataGenerator(modulesBasePath);
    }

    /// <summary>
    /// Loaded language configurations.
    /// </summary>
    public ObservableCollection<LanguageTreeItem> Languages { get; } = new();

    /// <summary>
    /// Whether the sidebar is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Status message for display.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// Adds an existing language folder.
    /// </summary>
    public void AddExistingLanguage(string folderPath)
    {
        var config = _translationService.LoadLanguageConfig(folderPath);
        if (config == null)
        {
            StatusMessage = "Failed to load language folder.";
            return;
        }

        // Check if already loaded
        if (Languages.Any(l => l.Config.LanguageCode == config.LanguageCode))
        {
            StatusMessage = $"Language {config.LanguageCode} is already loaded.";
            return;
        }

        var treeItem = CreateLanguageTreeItem(config);
        Languages.Add(treeItem);
        StatusMessage = $"Loaded {config.DisplayName}";
    }

    /// <summary>
    /// Creates a new language folder with stubs.
    /// </summary>
    public void CreateNewLanguage(string folderPath, string languageCode, string languageName)
    {
        var config = _languageDataGenerator.GenerateLanguageFolder(folderPath, languageCode, languageName);

        var treeItem = CreateLanguageTreeItem(config);
        Languages.Add(treeItem);
        StatusMessage = $"Created {config.DisplayName} with {config.TranslationFiles.Count} files";
    }

    /// <summary>
    /// Removes a language from the sidebar (doesn't delete files).
    /// </summary>
    [RelayCommand]
    private void RemoveLanguage(LanguageTreeItem? item)
    {
        if (item != null)
        {
            Languages.Remove(item);
            StatusMessage = $"Removed {item.Config.DisplayName}";
        }
    }

    /// <summary>
    /// Opens a translation file as a sheet.
    /// </summary>
    public void OpenFile(LanguageConfig config, string relativePath)
    {
        Console.WriteLine($"[TranslationsSidebar] OpenFile called: {relativePath}");

        // Build translation file path first
        var translationPath = Path.Combine(config.FolderPath, relativePath.Split('/').Skip(1).Aggregate((a, b) => $"{a}/{b}"));
        translationPath = translationPath.Replace('/', Path.DirectorySeparatorChar);

        // Check if translation file exists
        if (!File.Exists(translationPath))
        {
            Console.WriteLine($"[TranslationsSidebar] Translation file not found: {translationPath}");
            StatusMessage = $"Translation file not found!";

            // Raise event to show error - can create template for missing translation files
            SourceFileMissing?.Invoke(this, new SourceFileMissingEventArgs(
                relativePath, translationPath, isTranslationFileMissing: true, languageCode: config.LanguageCode));
            return;
        }

        // Resolve English source path and get expected path for error messages
        var (sourcePath, expectedPath) = _translationService.ResolveEnglishSourcePathWithExpected(relativePath);

        if (sourcePath == null)
        {
            Console.WriteLine($"[TranslationsSidebar] English source not found. Expected: {expectedPath}");
            StatusMessage = $"Source file not found!";

            // Raise event to show error - can't create template for missing source files
            SourceFileMissing?.Invoke(this, new SourceFileMissingEventArgs(
                relativePath, expectedPath, isTranslationFileMissing: false));
            return;
        }

        Console.WriteLine($"[TranslationsSidebar] English source: {sourcePath}");
        Console.WriteLine($"[TranslationsSidebar] Translation path: {translationPath}");

        // Create translation sheet
        var sheet = _translationService.CreateTranslationSheet(
            sourcePath,
            translationPath,
            config.LanguageCode,
            relativePath);

        Console.WriteLine($"[TranslationsSidebar] Created sheet with {sheet.Entries.Count} entries");

        // Raise event to open tab
        Console.WriteLine($"[TranslationsSidebar] Raising OpenTranslationSheetRequested event, has subscribers: {OpenTranslationSheetRequested != null}");
        OpenTranslationSheetRequested?.Invoke(this, new OpenTranslationSheetEventArgs(sheet, config));
    }

    /// <summary>
    /// Event raised when a source file is missing.
    /// </summary>
    public event EventHandler<SourceFileMissingEventArgs>? SourceFileMissing;

    /// <summary>
    /// Event raised when validation results are ready.
    /// </summary>
    public event EventHandler<LanguageValidationEventArgs>? ValidationCompleted;

    /// <summary>
    /// Validates a language configuration and raises an event with the results.
    /// </summary>
    public void ValidateLanguage(LanguageTreeItem languageItem)
    {
        var result = _translationService.ValidateLanguageConfig(languageItem.Config);
        var missingFiles = _translationService.FindMissingTranslationFiles(languageItem.Config);

        Console.WriteLine($"[TranslationsSidebar] Validation complete: {result.ValidEntries.Count} valid, {result.InvalidEntries.Count} invalid, {missingFiles.Count} missing");

        ValidationCompleted?.Invoke(this, new LanguageValidationEventArgs(result, missingFiles, languageItem));
    }

    /// <summary>
    /// Repairs the language_data.xml by removing invalid entries.
    /// </summary>
    public bool RepairLanguageData(LanguageConfig config, List<string> entriesToRemove)
    {
        var success = _translationService.RepairLanguageData(config, entriesToRemove);

        if (success)
        {
            RefreshLanguageTreeItem(config);
            StatusMessage = $"Repaired {config.DisplayName} - removed {entriesToRemove.Count} invalid entries";
        }

        return success;
    }

    /// <summary>
    /// Adds missing translation files to language_data.xml and creates templates.
    /// </summary>
    public bool AddMissingFiles(LanguageConfig config, List<string> filesToAdd)
    {
        var success = _translationService.AddMissingTranslationFiles(config, filesToAdd);

        if (success)
        {
            RefreshLanguageTreeItem(config);
            StatusMessage = $"Added {filesToAdd.Count} files to {config.DisplayName}";
        }

        return success;
    }

    /// <summary>
    /// Refreshes the language tree item after changes to the config.
    /// </summary>
    private void RefreshLanguageTreeItem(LanguageConfig config)
    {
        var langItem = Languages.FirstOrDefault(l => l.Config.LanguageCode == config.LanguageCode);
        if (langItem != null)
        {
            // Rebuild the tree structure
            langItem.Modules.Clear();
            var newItem = CreateLanguageTreeItem(config);
            foreach (var module in newItem.Modules)
            {
                langItem.Modules.Add(module);
            }
        }
    }

    private LanguageTreeItem CreateLanguageTreeItem(LanguageConfig config)
    {
        var item = new LanguageTreeItem(config, this);

        // Group files by module
        var filesByModule = config.TranslationFiles
            .GroupBy(f =>
            {
                var parts = f.Split('/');
                return parts.Length > 1 ? parts[1] : "Other";
            })
            .OrderBy(g => g.Key);

        foreach (var moduleGroup in filesByModule)
        {
            var moduleItem = new TranslationFileTreeItem
            {
                DisplayName = moduleGroup.Key,
                IsModule = true
            };

            foreach (var file in moduleGroup.OrderBy(f => f))
            {
                var fileName = Path.GetFileName(file);
                moduleItem.Children.Add(new TranslationFileTreeItem
                {
                    DisplayName = fileName,
                    RelativePath = file,
                    IsModule = false
                });
            }

            item.Modules.Add(moduleItem);
        }

        return item;
    }

    /// <summary>
    /// Gets the TranslationService for creating sheet ViewModels.
    /// </summary>
    public TranslationService TranslationService => _translationService;
}

/// <summary>
/// Tree item for a language in the sidebar.
/// </summary>
public partial class LanguageTreeItem : ViewModelBase
{
    private readonly TranslationsSidebarViewModel _parent;

    public LanguageTreeItem(LanguageConfig config, TranslationsSidebarViewModel parent)
    {
        Config = config;
        _parent = parent;
    }

    public LanguageConfig Config { get; }

    public string DisplayName => Config.DisplayName;

    public ObservableCollection<TranslationFileTreeItem> Modules { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Opens a file from this language.
    /// </summary>
    public void OpenFile(string relativePath)
    {
        _parent.OpenFile(Config, relativePath);
    }

    /// <summary>
    /// Validates the language configuration.
    /// </summary>
    [RelayCommand]
    public void Validate()
    {
        _parent.ValidateLanguage(this);
    }
}

/// <summary>
/// Tree item for a translation file or module folder.
/// </summary>
public partial class TranslationFileTreeItem : ViewModelBase
{
    public string DisplayName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsModule { get; set; }

    public ObservableCollection<TranslationFileTreeItem> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>
/// Event args for requesting to open a translation sheet.
/// </summary>
public class OpenTranslationSheetEventArgs : EventArgs
{
    public TranslationSheet Sheet { get; }
    public LanguageConfig Config { get; }

    public OpenTranslationSheetEventArgs(TranslationSheet sheet, LanguageConfig config)
    {
        Sheet = sheet;
        Config = config;
    }
}

/// <summary>
/// Event args when a source file is missing.
/// </summary>
public class SourceFileMissingEventArgs : EventArgs
{
    public string TranslationPath { get; }
    public string ExpectedSourcePath { get; }
    public bool IsTranslationFileMissing { get; }
    public string? LanguageCode { get; }

    public SourceFileMissingEventArgs(string translationPath, string expectedSourcePath, bool isTranslationFileMissing = false, string? languageCode = null)
    {
        TranslationPath = translationPath;
        ExpectedSourcePath = expectedSourcePath;
        IsTranslationFileMissing = isTranslationFileMissing;
        LanguageCode = languageCode;
    }
}

/// <summary>
/// Event args for language validation results.
/// </summary>
public class LanguageValidationEventArgs : EventArgs
{
    public LanguageValidationResult Result { get; }
    public List<string> MissingFiles { get; }
    public LanguageTreeItem LanguageItem { get; }

    public LanguageValidationEventArgs(LanguageValidationResult result, List<string> missingFiles, LanguageTreeItem languageItem)
    {
        Result = result;
        MissingFiles = missingFiles;
        LanguageItem = languageItem;
    }
}
