using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.App.Services;
using TORTools.Core.Models;
using TORTools.Core.Services;
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
    private WorkspaceConfig _config;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _rowCountText = "";

    [ObservableProperty]
    private string _workspacePath = "";

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = "";

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public ObservableCollection<CatalogNode> Catalogs { get; } = new();

    public ObservableCollection<FileTabViewModel> OpenTabs { get; } = new();

    public bool HasOpenTabs => OpenTabs.Count > 0;

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

        var totalFiles = catalogGroups.Sum(c => c.Files.Count);
        StatusMessage = $"Loaded {totalFiles} XML files in {catalogGroups.Count} catalogs";
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

        // Subscribe to cross-reference navigation events
        newTab.NavigateToCrossReference += OnNavigateToCrossReference;

        OpenTabs.Add(newTab);
        ActiveTab = newTab;
        OnPropertyChanged(nameof(HasOpenTabs));

        StatusMessage = $"Opened {newTab.Title}";
        RowCountText = $"{newTab.Rows.Count} entries";
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
        foreach (var catalog in Catalogs)
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
            // Apply current search filter to newly active tab
            value.FilterText = SearchText;
            UpdateRowCountText();
        }
        else
        {
            RowCountText = "";
        }
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
