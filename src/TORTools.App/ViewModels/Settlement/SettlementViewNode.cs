using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TORTools.App.ViewModels.Settlement;

/// <summary>
/// The type of settlement editor view.
/// </summary>
public enum SettlementViewType
{
    /// <summary>Map view with world map and settlement markers.</summary>
    MapView,

    /// <summary>Table view for mass editing.</summary>
    TableView
}

/// <summary>
/// Represents a settlement editor view node in the sidebar.
/// Similar to FileNode but opens settlement editor tabs.
/// </summary>
public partial class SettlementViewNode : ObservableObject
{
    /// <summary>
    /// Display name shown in the sidebar (e.g., "Map View", "Table View").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// The type of view this node opens.
    /// </summary>
    public SettlementViewType ViewType { get; }

    /// <summary>
    /// Path to the settlement file to load.
    /// </summary>
    public string SettlementFilePath { get; }

    /// <summary>
    /// Description shown in tooltip.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Event raised when this view is opened.
    /// </summary>
    public event EventHandler<SettlementViewNode>? ViewOpened;

    public SettlementViewNode(string displayName, SettlementViewType viewType, string settlementFilePath, string description)
    {
        DisplayName = displayName;
        ViewType = viewType;
        SettlementFilePath = settlementFilePath;
        Description = description;
    }

    /// <summary>
    /// Opens this settlement editor view.
    /// </summary>
    [RelayCommand]
    public void Open()
    {
        ViewOpened?.Invoke(this, this);
    }

    /// <summary>
    /// Display name for TreeView DataTemplate binding.
    /// </summary>
    public string Name => DisplayName;
}

/// <summary>
/// Represents the Settlement Editor catalog node in the sidebar.
/// Contains Map View and Table View child nodes.
/// </summary>
public partial class SettlementCatalogNode : ObservableObject
{
    /// <summary>
    /// Display name shown in the sidebar.
    /// </summary>
    public string Name { get; } = "Settlement Editor";

    /// <summary>
    /// Whether this node is expanded in the tree.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Child view nodes (Map View, Table View).
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<SettlementViewNode> Views { get; } = new();

    /// <summary>
    /// Event raised when a view is opened.
    /// </summary>
    public event EventHandler<SettlementViewNode>? ViewOpened;

    /// <summary>
    /// Creates the settlement catalog node with Map View and Table View children.
    /// </summary>
    public SettlementCatalogNode(string settlementFilePath)
    {
        var mapView = new SettlementViewNode(
            "Map View",
            SettlementViewType.MapView,
            settlementFilePath,
            "Interactive world map with settlement markers. Click to select, view details in popup.");

        var tableView = new SettlementViewNode(
            "Table View",
            SettlementViewType.TableView,
            settlementFilePath,
            "Data grid for mass editing settlements. Multi-select, batch scene assignment.");

        mapView.ViewOpened += (s, e) => ViewOpened?.Invoke(s, e);
        tableView.ViewOpened += (s, e) => ViewOpened?.Invoke(s, e);

        Views.Add(mapView);
        Views.Add(tableView);
    }
}
