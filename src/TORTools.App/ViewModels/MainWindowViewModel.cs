using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.App.Services;
using TORTools.App.ViewModels.Settlement;
using TORTools.App.ViewModels.Translation;
using TORTools.Core.Models;
using TORTools.Core.Models.Translation;
using TORTools.Core.Services;
using TORTools.Core.Services.Settlement;
using TORTools.Core.Services.Translation;
using TORTools.Core.Workspace;

namespace TORTools.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IIconService? _iconService;
    private readonly ItemCatalogService _itemCatalogService;
    private readonly FactionCatalogService _factionCatalogService;
    private readonly AbilityCatalogService _abilityCatalogService;
    private readonly ItemTraitCatalogService _itemTraitCatalogService;
    private readonly BannerImageService? _bannerImageService;
    private readonly IXmlDocumentService _xmlDocumentService;
    private readonly TranslationCacheService? _translationCacheService;
    private WorkspaceConfig _config;

    /// <summary>
    /// ViewModel for the Translations sidebar section.
    /// </summary>
    public TranslationsSidebarViewModel? TranslationsSidebar { get; private set; }

    /// <summary>
    /// Open translation sheet tabs.
    /// </summary>
    public ObservableCollection<TranslationSheetTabViewModel> TranslationTabs { get; } = new();

    /// <summary>
    /// The active translation sheet tab (if any).
    /// </summary>
    [ObservableProperty]
    private TranslationSheetTabViewModel? _activeTranslationTab;

    /// <summary>
    /// Settlement Editor catalog node in sidebar.
    /// </summary>
    private SettlementCatalogNode? _settlementCatalog;

    /// <summary>
    /// Shared settlement edit context for Map and Table views.
    /// </summary>
    private SettlementEditContext? _settlementContext;

    /// <summary>
    /// Open settlement editor tabs.
    /// </summary>
    public ObservableCollection<SettlementMapTabViewModel> SettlementTabs { get; } = new();

    /// <summary>
    /// The active settlement editor tab (if any).
    /// </summary>
    [ObservableProperty]
    private SettlementMapTabViewModel? _activeSettlementTab;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _rowCountText = "";

    [ObservableProperty]
    private string _workspacePath = "";

    /// <summary>
    /// Error message to display prominently in the main content area.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    /// <summary>
    /// Whether there's an error to display.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Whether the current error allows creating a template file.
    /// </summary>
    [ObservableProperty]
    private bool _canCreateTemplate;

    /// <summary>
    /// The path where a template should be created.
    /// </summary>
    private string? _missingFilePath;

    /// <summary>
    /// The relative path for template creation.
    /// </summary>
    private string? _missingFileRelativePath;

    /// <summary>
    /// The language code for template creation.
    /// </summary>
    private string? _missingFileLanguageCode;

    /// <summary>
    /// Whether validation results are being shown.
    /// </summary>
    [ObservableProperty]
    private bool _showValidationResults;

    /// <summary>
    /// Validation result message.
    /// </summary>
    [ObservableProperty]
    private string _validationMessage = "";

    /// <summary>
    /// Whether repair is possible (there are invalid entries).
    /// </summary>
    [ObservableProperty]
    private bool _canRepair;

    /// <summary>
    /// The current validation event args for repair.
    /// </summary>
    private LanguageValidationEventArgs? _currentValidation;

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = "";

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public ObservableCollection<object> Catalogs { get; } = new();

    public ObservableCollection<FileTabViewModel> OpenTabs { get; } = new();

    public bool HasOpenTabs => OpenTabs.Count > 0;

    /// <summary>
    /// Whether any tab (file, translation, or settlement) is open.
    /// </summary>
    public bool HasAnyOpenTab => ActiveTab != null || ActiveTranslationTab != null || ActiveSettlementTab != null;

    /// <summary>
    /// The current tab content (file, translation, or settlement tab).
    /// </summary>
    public object? CurrentTabContent => (object?)ActiveSettlementTab ?? (object?)ActiveTranslationTab ?? ActiveTab;

    /// <summary>
    /// The current tab title.
    /// </summary>
    public string CurrentTabTitle => ActiveSettlementTab?.FullTitle ?? ActiveTranslationTab?.FullTitle ?? ActiveTab?.Title ?? "";

    /// <summary>
    /// Whether the current tab has unsaved changes.
    /// </summary>
    public bool CurrentTabHasUnsavedChanges => ActiveSettlementTab?.HasUnsavedChanges ?? ActiveTranslationTab?.HasUnsavedChanges ?? ActiveTab?.HasUnsavedChanges ?? false;

    /// <summary>
    /// Event raised when focus should be given to the search box.
    /// </summary>
    public event EventHandler? FocusSearchRequested;

    public MainWindowViewModel() : this(new WorkspaceService())
    {
    }

    public MainWindowViewModel(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
        _config = _workspaceService.LoadConfig();
        _itemCatalogService = new ItemCatalogService();
        _factionCatalogService = new FactionCatalogService();
        _xmlDocumentService = new XmlDocumentService();
        _abilityCatalogService = new AbilityCatalogService("");
        _itemTraitCatalogService = new ItemTraitCatalogService("");

        // Initialize icon service if TOR_Armory path is available
        if (!string.IsNullOrEmpty(_config.TorArmoryPath))
        {
            var guiPath = Path.Combine(_config.TorArmoryPath, "GUI");
            if (Directory.Exists(guiPath))
            {
                _iconService = new IconService(guiPath);
            }

            // Initialize item catalog for equipment set validation
            var moduleDataPath = Path.Combine(_config.TorArmoryPath, "ModuleData");
            if (Directory.Exists(moduleDataPath))
            {
                _itemCatalogService.LoadItems(moduleDataPath);
            }

            // Initialize banner image service for faction banner display
            var assetSourcesPath = Path.Combine(_config.TorArmoryPath, "AssetSources");
            if (Directory.Exists(assetSourcesPath))
            {
                _bannerImageService = new BannerImageService(assetSourcesPath);
            }
        }

        // Initialize faction catalog and ability catalog if TOR_Core path is available
        if (!string.IsNullOrEmpty(_config.TorCorePath))
        {
            var coreModuleDataPath = Path.Combine(_config.TorCorePath, "ModuleData");
            var armoryAssetSourcesPath = !string.IsNullOrEmpty(_config.TorArmoryPath)
                ? Path.Combine(_config.TorArmoryPath, "AssetSources")
                : null;
            if (Directory.Exists(coreModuleDataPath))
            {
                _factionCatalogService.LoadFactions(coreModuleDataPath, armoryAssetSourcesPath);
                _abilityCatalogService = new AbilityCatalogService(coreModuleDataPath);
                _itemTraitCatalogService = new ItemTraitCatalogService(coreModuleDataPath);
            }
        }

        // Initialize translations sidebar and cache service
        if (!string.IsNullOrEmpty(_config.BannerlordPath))
        {
            var modulesPath = Path.Combine(_config.BannerlordPath, "Modules");
            if (Directory.Exists(modulesPath))
            {
                TranslationsSidebar = new TranslationsSidebarViewModel(modulesPath);
                TranslationsSidebar.OpenTranslationSheetRequested += OnOpenTranslationSheetRequested;
                TranslationsSidebar.SourceFileMissing += OnSourceFileMissing;
                TranslationsSidebar.ValidationCompleted += OnValidationCompleted;

                // Initialize translation cache service in TORTools module
                var torToolsPath = Path.Combine(modulesPath, "TORTools");
                if (Directory.Exists(torToolsPath))
                {
                    _translationCacheService = new TranslationCacheService(torToolsPath);
                    Console.WriteLine($"[MainVM] Translation cache initialized at {torToolsPath}");
                }
            }
        }

        LoadCatalogs();
    }

    private void LoadCatalogs()
    {
        Catalogs.Clear();

        var validation = _workspaceService.ValidateWorkspace(_config);

        if (!validation.IsValid)
        {
            StatusMessage = validation.Errors.FirstOrDefault() ?? "Workspace not configured";
            WorkspacePath = "Not configured";
            return;
        }

        WorkspacePath = _config.BannerlordPath ?? "";

        var catalogGroups = _workspaceService.GetCatalogs(_config);

        foreach (var catalog in catalogGroups)
        {
            var catalogNode = new CatalogNode(catalog.Name);

            foreach (var file in catalog.Files)
            {
                var fileNode = new FileNode(file.DisplayName, file.FilePath, file.Repository);
                fileNode.FileOpened += OnFileNodeOpened;
                catalogNode.Files.Add(fileNode);
            }

            Catalogs.Add(catalogNode);
        }

        // Add Settlement Editor catalog
        if (!string.IsNullOrEmpty(_config.TorCorePath))
        {
            var settlementPath = Path.Combine(_config.TorCorePath, "ModuleData", "tor_settlements.xml");
            if (File.Exists(settlementPath))
            {
                _settlementCatalog = new SettlementCatalogNode(settlementPath);
                _settlementCatalog.ViewOpened += OnSettlementViewOpened;
                Catalogs.Add(_settlementCatalog);
            }
        }

        // Add Translations catalog at the end
        if (TranslationsSidebar != null)
        {
            Catalogs.Add(TranslationsSidebar);
        }

        var totalFiles = catalogGroups.Sum(c => c.Files.Count);
        StatusMessage = $"Loaded {totalFiles} XML files in {catalogGroups.Count} catalogs";
    }

    private async void OnSettlementViewOpened(object? sender, SettlementViewNode viewNode)
    {
        // Create shared context if not exists
        if (_settlementContext == null)
        {
            var service = new SettlementService();
            _settlementContext = new SettlementEditContext(service);
            await _settlementContext.LoadAsync(viewNode.SettlementFilePath);
        }

        // Check if tab already exists
        var existingTab = SettlementTabs.FirstOrDefault(t => t.Title == "Settlement Map");
        if (existingTab != null)
        {
            ActiveSettlementTab = existingTab;
            ActiveTab = null;
            ActiveTranslationTab = null;
            return;
        }

        // Create new tab based on view type
        if (viewNode.ViewType == SettlementViewType.MapView)
        {
            var newTab = new SettlementMapTabViewModel(_settlementContext);
            SettlementTabs.Add(newTab);
            ActiveSettlementTab = newTab;
            ActiveTab = null;
            ActiveTranslationTab = null;

            StatusMessage = $"Opened Settlement Map View - {_settlementContext.TotalCount} settlements";

            OnPropertyChanged(nameof(HasAnyOpenTab));
            OnPropertyChanged(nameof(CurrentTabContent));
        }
        // TODO: Add TableView support in future iteration
    }

    private void OnFileNodeOpened(object? sender, string filePath)
    {
        OpenFile(filePath);
    }

    public void OpenFile(string filePath)
    {
        // Check if already open
        var existingTab = OpenTabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existingTab != null)
        {
            ActiveTab = existingTab;
            return;
        }

        // Open new tab
        var newTab = new FileTabViewModel(filePath);

        // Assign icon service if available
        newTab.IconService = _iconService;

        // Assign item catalog service for equipment set validation
        newTab.ItemCatalogService = _itemCatalogService;

        // Assign banner image service for faction banner display
        newTab.BannerImageService = _bannerImageService;

        // Assign ability catalog service for ability icons
        newTab.AbilityCatalogService = _abilityCatalogService;

        // Assign item trait catalog service for trait icons
        newTab.ItemTraitCatalogService = _itemTraitCatalogService;

        // Assign faction catalog service for kingdom color inheritance
        newTab.FactionCatalogService = _factionCatalogService;

        // Assign XML document service for file path resolution
        newTab.XmlDocumentService = _xmlDocumentService;

        // Subscribe to cross-reference navigation events
        newTab.NavigateToCrossReference += OnNavigateToCrossReference;

        OpenTabs.Add(newTab);
        ActiveTab = newTab;
        OnPropertyChanged(nameof(HasOpenTabs));

        StatusMessage = $"Opened {newTab.Title}";
        RowCountText = $"{newTab.Rows.Count} entries";
    }

    /// <summary>
    /// Handles request to open a translation sheet tab.
    /// </summary>
    private void OnOpenTranslationSheetRequested(object? sender, OpenTranslationSheetEventArgs e)
    {
        OpenTranslationSheet(e.Sheet, e.Config);
    }

    /// <summary>
    /// Handles when a source file is missing for translation.
    /// </summary>
    private void OnSourceFileMissing(object? sender, SourceFileMissingEventArgs e)
    {
        // Store info for template creation
        _missingFilePath = e.ExpectedSourcePath;
        _missingFileRelativePath = e.TranslationPath;
        _missingFileLanguageCode = e.LanguageCode;
        CanCreateTemplate = e.IsTranslationFileMissing;

        // Show error message with different text based on file type
        if (e.IsTranslationFileMissing)
        {
            ErrorMessage = $"Translation file not found!\n\nExpected location:\n{e.ExpectedSourcePath}\n\nYou can create an empty template file at this location.";
        }
        else
        {
            ErrorMessage = $"Source file not found!\n\nExpected location:\n{e.ExpectedSourcePath}\n\nPlease ensure the English source file exists at the expected path.";
        }
        StatusMessage = "File not found";

        // Log for debugging
        Console.WriteLine($"[Translation] File missing! IsTranslation: {e.IsTranslationFileMissing}");
        Console.WriteLine($"[Translation] Translation entry: {e.TranslationPath}");
        Console.WriteLine($"[Translation] Expected file: {e.ExpectedSourcePath}");
    }

    /// <summary>
    /// Clears the error message.
    /// </summary>
    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = "";
        CanCreateTemplate = false;
        _missingFilePath = null;
        _missingFileRelativePath = null;
        _missingFileLanguageCode = null;
    }

    /// <summary>
    /// Creates a template translation file at the missing path.
    /// </summary>
    [RelayCommand]
    private void CreateTemplate()
    {
        if (string.IsNullOrEmpty(_missingFilePath) || string.IsNullOrEmpty(_missingFileRelativePath))
            return;

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_missingFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Get the language name (default to language code if not found)
            var languageName = _missingFileLanguageCode ?? "Unknown";
            if (TranslationsSidebar != null)
            {
                var lang = TranslationsSidebar.Languages.FirstOrDefault(l => l.Config.LanguageCode == _missingFileLanguageCode);
                if (lang != null)
                {
                    languageName = lang.Config.LanguageName;
                }
            }

            // Create empty translation template
            var template = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">
                  <tags>
                    <tag language="{languageName}" />
                  </tags>
                  <strings>
                    <!-- Translation entries will be added here -->
                  </strings>
                </base>
                """;

            File.WriteAllText(_missingFilePath, template);

            StatusMessage = $"Created template: {Path.GetFileName(_missingFilePath)}";
            Console.WriteLine($"[Translation] Created template at: {_missingFilePath}");

            // Clear error and refresh
            ClearError();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create template:\n{ex.Message}";
            Console.WriteLine($"[Translation] Failed to create template: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles validation completion.
    /// </summary>
    private void OnValidationCompleted(object? sender, LanguageValidationEventArgs e)
    {
        _currentValidation = e;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Validation Results for {e.Result.LanguageName}");
        sb.AppendLine();
        sb.AppendLine($"Valid entries: {e.Result.ValidEntries.Count}");
        sb.AppendLine($"Invalid entries: {e.Result.InvalidEntries.Count}");
        sb.AppendLine($"Missing files: {e.MissingFiles.Count}");

        if (e.Result.HasInvalidEntries)
        {
            sb.AppendLine();
            sb.AppendLine("Invalid entries (will be REMOVED):");
            foreach (var invalid in e.Result.InvalidEntries.Take(10))
            {
                sb.AppendLine($"  - {invalid.RelativePath}");
            }
            if (e.Result.InvalidEntries.Count > 10)
            {
                sb.AppendLine($"  ... and {e.Result.InvalidEntries.Count - 10} more");
            }
        }

        if (e.MissingFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Missing files (will be ADDED):");
            foreach (var missing in e.MissingFiles.Take(10))
            {
                sb.AppendLine($"  + {missing}");
            }
            if (e.MissingFiles.Count > 10)
            {
                sb.AppendLine($"  ... and {e.MissingFiles.Count - 10} more");
            }
        }

        ValidationMessage = sb.ToString();
        CanRepair = e.Result.HasInvalidEntries || e.MissingFiles.Count > 0;
        ShowValidationResults = true;

        if (e.Result.HasInvalidEntries && e.MissingFiles.Count > 0)
            StatusMessage = $"Found {e.Result.InvalidEntries.Count} invalid, {e.MissingFiles.Count} missing";
        else if (e.Result.HasInvalidEntries)
            StatusMessage = $"Found {e.Result.InvalidEntries.Count} invalid entries";
        else if (e.MissingFiles.Count > 0)
            StatusMessage = $"Found {e.MissingFiles.Count} missing files";
        else
            StatusMessage = "Validation passed - all entries valid";
    }

    /// <summary>
    /// Closes the validation results panel.
    /// </summary>
    [RelayCommand]
    private void CloseValidation()
    {
        ShowValidationResults = false;
        CanRepair = false;
        _currentValidation = null;
    }

    /// <summary>
    /// Repairs the language_data.xml by removing invalid entries and adding missing files.
    /// </summary>
    [RelayCommand]
    private void RepairLanguageData()
    {
        if (_currentValidation == null)
            return;

        var hasInvalid = _currentValidation.Result.HasInvalidEntries;
        var hasMissing = _currentValidation.MissingFiles.Count > 0;

        if (!hasInvalid && !hasMissing)
            return;

        var config = _currentValidation.LanguageItem.Config;
        var messages = new List<string>();
        bool anyFailure = false;

        // Remove invalid entries
        if (hasInvalid)
        {
            var entriesToRemove = _currentValidation.Result.InvalidEntries
                .Select(e => e.RelativePath)
                .ToList();

            var removeSuccess = TranslationsSidebar?.RepairLanguageData(config, entriesToRemove) ?? false;
            if (removeSuccess)
            {
                messages.Add($"Removed {entriesToRemove.Count} invalid");
            }
            else
            {
                anyFailure = true;
            }
        }

        // Add missing files
        if (hasMissing)
        {
            var addSuccess = TranslationsSidebar?.AddMissingFiles(config, _currentValidation.MissingFiles) ?? false;
            if (addSuccess)
            {
                messages.Add($"Added {_currentValidation.MissingFiles.Count} missing");
            }
            else
            {
                anyFailure = true;
            }
        }

        if (anyFailure)
        {
            ErrorMessage = "Some repair operations failed. Check console for details.";
        }
        else
        {
            StatusMessage = string.Join(", ", messages);
            ShowValidationResults = false;
            CanRepair = false;
            _currentValidation = null;
        }
    }

    /// <summary>
    /// Opens a translation sheet as a tab.
    /// </summary>
    public void OpenTranslationSheet(TranslationSheet sheet, LanguageConfig config)
    {
        Console.WriteLine($"[MainVM] OpenTranslationSheet called: {sheet.FileName}, lang: {config.LanguageCode}");

        // Check if already open
        var tabKey = $"{config.LanguageCode}:{sheet.FileName}";
        var existingTab = TranslationTabs.FirstOrDefault(t =>
            t.LanguageCode == config.LanguageCode && t.FileName == sheet.FileName);

        if (existingTab != null)
        {
            Console.WriteLine($"[MainVM] Tab already open, switching...");
            ActiveTranslationTab = existingTab;
            ActiveTab = null; // Deselect file tab
            StatusMessage = $"Switched to {existingTab.Title}";
            return;
        }

        // Create new tab
        Console.WriteLine($"[MainVM] Creating new TranslationSheetTabViewModel...");
        var newTab = new TranslationSheetTabViewModel(
            sheet,
            TranslationsSidebar!.TranslationService,
            config,
            _translationCacheService);

        TranslationTabs.Add(newTab);
        Console.WriteLine($"[MainVM] Added tab, setting ActiveTranslationTab...");
        ActiveTranslationTab = newTab;
        ActiveTab = null; // Deselect file tab

        Console.WriteLine($"[MainVM] ActiveTranslationTab set, HasAnyOpenTab: {HasAnyOpenTab}, CurrentTabContent type: {CurrentTabContent?.GetType().Name}");

        StatusMessage = $"Opened {newTab.Title} - {newTab.CompletionText} complete";
        RowCountText = $"{sheet.Entries.Count} entries";
    }

    /// <summary>
    /// Closes a translation sheet tab.
    /// </summary>
    [RelayCommand]
    private void CloseTranslationTab(TranslationSheetTabViewModel? tab)
    {
        if (tab == null) return;

        var index = TranslationTabs.IndexOf(tab);
        TranslationTabs.Remove(tab);
        tab.Dispose();

        // Select adjacent tab
        if (TranslationTabs.Count > 0)
        {
            ActiveTranslationTab = TranslationTabs[Math.Min(index, TranslationTabs.Count - 1)];
        }
        else
        {
            ActiveTranslationTab = null;
        }
    }

    /// <summary>
    /// Handles navigation to a cross-referenced entry in another file.
    /// Searches through multiple target files until the entry is found.
    /// </summary>
    private void OnNavigateToCrossReference(object? sender, CrossReferenceNavigationEventArgs e)
    {
        Console.WriteLine($"[Navigation] Navigating to [{string.Join(", ", e.TargetFiles)}], key {e.TargetKeyField} = {e.TargetValue}");

        // Search through all target files until we find the entry
        foreach (var targetFile in e.TargetFiles)
        {
            var targetFilePath = FindFileByName(targetFile);
            if (targetFilePath == null)
            {
                Console.WriteLine($"[Navigation] File not found: {targetFile}, trying next...");
                continue;
            }

            // Open or switch to the target file
            OpenFile(targetFilePath);

            // Find and select the matching row
            if (ActiveTab != null)
            {
                var rowIndex = FindRowByKeyValue(ActiveTab, e.TargetKeyField, e.TargetValue);
                if (rowIndex >= 0)
                {
                    ActiveTab.SelectedIndex = rowIndex;
                    StatusMessage = $"Navigated to {e.TargetValue} in {targetFile}";
                    Console.WriteLine($"[Navigation] Found in {targetFile}, selected row {rowIndex}");
                    return; // Found it!
                }
                else
                {
                    Console.WriteLine($"[Navigation] Entry not found in {targetFile}, trying next...");
                }
            }
        }

        // Not found in any target file
        StatusMessage = $"Entry not found: {e.TargetValue}";
        Console.WriteLine($"[Navigation] Entry not found in any target file: {e.TargetValue}");
    }

    /// <summary>
    /// Finds a file path by its file name across all catalogs.
    /// </summary>
    private string? FindFileByName(string fileName)
    {
        foreach (var catalog in Catalogs.OfType<CatalogNode>())
        {
            foreach (var file in catalog.Files)
            {
                if (Path.GetFileName(file.FilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return file.FilePath;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds a row index by matching a key field value.
    /// </summary>
    private static int FindRowByKeyValue(FileTabViewModel tab, string keyField, string value)
    {
        for (int i = 0; i < tab.Rows.Count; i++)
        {
            var row = tab.Rows[i];
            var cellValue = row[keyField];
            if (cellValue?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                return i;
            }
        }
        return -1;
    }

    partial void OnActiveTabChanged(FileTabViewModel? value)
    {
        if (value != null)
        {
            // Deselect translation tab when selecting file tab
            ActiveTranslationTab = null;
            // Apply current search filter to newly active tab
            value.FilterText = SearchText;
            UpdateRowCountText();
        }
        else
        {
            RowCountText = "";
        }

        // Notify computed properties
        OnPropertyChanged(nameof(HasAnyOpenTab));
        OnPropertyChanged(nameof(CurrentTabContent));
        OnPropertyChanged(nameof(CurrentTabTitle));
        OnPropertyChanged(nameof(CurrentTabHasUnsavedChanges));
    }

    partial void OnActiveTranslationTabChanged(TranslationSheetTabViewModel? value)
    {
        if (value != null)
        {
            // Deselect file tab when selecting translation tab
            ActiveTab = null;
            RowCountText = $"{value.TotalEntries} entries";
        }

        // Notify computed properties
        OnPropertyChanged(nameof(HasAnyOpenTab));
        OnPropertyChanged(nameof(CurrentTabContent));
        OnPropertyChanged(nameof(CurrentTabTitle));
        OnPropertyChanged(nameof(CurrentTabHasUnsavedChanges));
    }

    partial void OnActiveSettlementTabChanged(SettlementMapTabViewModel? value)
    {
        if (value != null)
        {
            // Deselect other tab types when selecting settlement tab
            ActiveTab = null;
            ActiveTranslationTab = null;
            RowCountText = "";
        }

        // Notify computed properties
        OnPropertyChanged(nameof(HasAnyOpenTab));
        OnPropertyChanged(nameof(CurrentTabContent));
        OnPropertyChanged(nameof(CurrentTabTitle));
        OnPropertyChanged(nameof(CurrentTabHasUnsavedChanges));
    }

    partial void OnSearchTextChanged(string value)
    {
        // Apply filter to active tab
        if (ActiveTab != null)
        {
            ActiveTab.FilterText = value;
            UpdateRowCountText();
        }
    }

    private void UpdateRowCountText()
    {
        if (ActiveTab == null)
        {
            RowCountText = "";
            return;
        }

        var visibleCount = ActiveTab.FilteredRows?.Count ?? ActiveTab.Rows.Count;
        var totalCount = ActiveTab.Rows.Count;

        if (visibleCount < totalCount)
        {
            RowCountText = $"{visibleCount} of {totalCount} entries";
        }
        else
        {
            RowCountText = $"{totalCount} entries";
        }
    }

    [RelayCommand]
    private void Save()
    {
        ActiveTab?.Save();

        // Refresh cross-references on all other tabs so they pick up newly added entries
        RefreshAllTabsCrossReferences();

        StatusMessage = "Saved";
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var tab in OpenTabs)
        {
            tab.Save();
        }

        // Refresh cross-references on all tabs
        RefreshAllTabsCrossReferences();

        StatusMessage = "All files saved";
    }

    /// <summary>
    /// Refreshes cross-reference data on all open tabs.
    /// Call this after any save operation so that newly added entries
    /// appear in autocomplete dropdowns across tabs.
    /// </summary>
    private void RefreshAllTabsCrossReferences()
    {
        foreach (var tab in OpenTabs)
        {
            tab.RefreshCrossReferences();
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (ActiveTab == null) return;

        if (ActiveTab.UndoRedoService.CanUndo)
        {
            var description = ActiveTab.UndoRedoService.UndoDescription;
            ActiveTab.Undo();
            StatusMessage = $"Undid: {description}";
            RowCountText = $"{ActiveTab.Rows.Count} entries";
        }
        else
        {
            StatusMessage = "Nothing to undo";
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (ActiveTab == null) return;

        if (ActiveTab.UndoRedoService.CanRedo)
        {
            var description = ActiveTab.UndoRedoService.RedoDescription;
            ActiveTab.Redo();
            StatusMessage = $"Redid: {description}";
            RowCountText = $"{ActiveTab.Rows.Count} entries";
        }
        else
        {
            StatusMessage = "Nothing to redo";
        }
    }

    [RelayCommand]
    private void AddRow()
    {
        if (ActiveTab == null) return;

        ActiveTab.AddRow();
        StatusMessage = "Added new row";
        RowCountText = $"{ActiveTab.Rows.Count} entries";
    }

    [RelayCommand]
    private void DuplicateRow()
    {
        if (ActiveTab == null) return;

        if (ActiveTab.SelectedIndex < 0)
        {
            StatusMessage = "Select a row to duplicate";
            return;
        }

        ActiveTab.DuplicateRow();
        StatusMessage = "Duplicated row";
        RowCountText = $"{ActiveTab.Rows.Count} entries";
    }

    [RelayCommand]
    private void DeleteRow()
    {
        if (ActiveTab == null) return;

        if (ActiveTab.SelectedIndex < 0)
        {
            StatusMessage = "Select a row to delete";
            return;
        }

        ActiveTab.DeleteRow();
        StatusMessage = "Deleted row";
        RowCountText = $"{ActiveTab.Rows.Count} entries";
    }

    [RelayCommand]
    private void OpenWorkspaceSettings()
    {
        // TODO: Open workspace settings dialog
        StatusMessage = "Opening workspace settings...";
    }

    [RelayCommand]
    private void Exit()
    {
        // TODO: Check for unsaved changes before exit
        Environment.Exit(0);
    }

    [RelayCommand]
    private void About()
    {
        StatusMessage = "TOR Tools - XML Editor for The Old Realms mod";
    }

    [RelayCommand]
    private void RefreshWorkspace()
    {
        _config = _workspaceService.LoadConfig();
        LoadCatalogs();
    }

    /// <summary>
    /// Adds an existing language folder to the translations sidebar.
    /// Called from the UI after folder picker selection.
    /// </summary>
    public void AddExistingLanguageFolder(string folderPath)
    {
        TranslationsSidebar?.AddExistingLanguage(folderPath);
    }

    /// <summary>
    /// Creates a new language folder with translation stubs.
    /// Called from the UI after folder picker selection and language code entry.
    /// </summary>
    public void CreateNewLanguageFolder(string folderPath, string languageCode, string languageName)
    {
        TranslationsSidebar?.CreateNewLanguage(folderPath, languageCode, languageName);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
    }

    [RelayCommand]
    private void FocusSearch()
    {
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseTab(FileTabViewModel? tab)
    {
        if (tab == null) return;

        // Unsubscribe from events
        tab.NavigateToCrossReference -= OnNavigateToCrossReference;

        // TODO: Check for unsaved changes
        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        tab.Dispose();

        // Select adjacent tab
        if (OpenTabs.Count > 0)
        {
            ActiveTab = OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
        }
        else
        {
            ActiveTab = null;
        }

        OnPropertyChanged(nameof(HasOpenTabs));
    }
}

/// <summary>
/// Represents a catalog in the sidebar (e.g., "Item Catalog", "Unit Catalog").
/// </summary>
public partial class CatalogNode : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<FileNode> Files { get; } = new();

    public CatalogNode(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Represents a file within a catalog.
/// </summary>
public partial class FileNode : ObservableObject
{
    public string DisplayName { get; }
    public string FilePath { get; }
    public string Repository { get; }

    public event EventHandler<string>? FileOpened;

    public FileNode(string displayName, string filePath, string repository)
    {
        DisplayName = displayName;
        FilePath = filePath;
        Repository = repository;
    }

    [RelayCommand]
    private void Open()
    {
        FileOpened?.Invoke(this, FilePath);
    }
}
