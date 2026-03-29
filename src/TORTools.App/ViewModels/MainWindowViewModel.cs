using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Models;
using TORTools.Core.Workspace;

namespace TORTools.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private WorkspaceConfig _config;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _rowCountText = "";

    [ObservableProperty]
    private string _workspacePath = "";

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    public ObservableCollection<CatalogNode> Catalogs { get; } = new();

    public ObservableCollection<FileTabViewModel> OpenTabs { get; } = new();

    public bool HasOpenTabs => OpenTabs.Count > 0;

    public MainWindowViewModel() : this(new WorkspaceService())
    {
    }

    public MainWindowViewModel(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
        _config = _workspaceService.LoadConfig();
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
        OpenTabs.Add(newTab);
        ActiveTab = newTab;
        OnPropertyChanged(nameof(HasOpenTabs));

        StatusMessage = $"Opened {newTab.Title}";
        RowCountText = $"{newTab.Rows.Count} entries";
    }

    partial void OnActiveTabChanged(FileTabViewModel? value)
    {
        if (value != null)
        {
            RowCountText = $"{value.Rows.Count} entries";
        }
        else
        {
            RowCountText = "";
        }
    }

    [RelayCommand]
    private void Save()
    {
        ActiveTab?.Save();
        StatusMessage = "Saved";
    }

    [RelayCommand]
    private void SaveAll()
    {
        foreach (var tab in OpenTabs)
        {
            tab.Save();
        }
        StatusMessage = "All files saved";
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
    private void CloseTab(FileTabViewModel? tab)
    {
        if (tab == null) return;

        // TODO: Check for unsaved changes
        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

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
