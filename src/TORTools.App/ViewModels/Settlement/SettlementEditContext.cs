using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TORTools.Core.Models.Settlement;
using TORTools.Core.Services.Settlement;

namespace TORTools.App.ViewModels.Settlement;

/// <summary>
/// Shared state container for settlement editing.
/// Both Map View and Table View reference this same context to keep data in sync.
/// </summary>
public partial class SettlementEditContext : ObservableObject
{
    private readonly SettlementService _settlementService;

    /// <summary>
    /// All loaded settlements.
    /// </summary>
    public ObservableCollection<SettlementEntry> AllSettlements { get; } = new();

    /// <summary>
    /// Filtered settlements based on current filter criteria.
    /// </summary>
    public ObservableCollection<SettlementEntry> FilteredSettlements { get; } = new();

    /// <summary>
    /// Currently selected settlement IDs (for multi-select).
    /// </summary>
    public HashSet<string> SelectedIds { get; } = new();

    /// <summary>
    /// Currently hovered settlement (for map tooltip and highlighting).
    /// </summary>
    [ObservableProperty]
    private SettlementEntry? _hoveredSettlement;

    /// <summary>
    /// The path to the loaded settlements file.
    /// </summary>
    [ObservableProperty]
    private string _filePath = "";

    /// <summary>
    /// Whether data is currently loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Error message if loading failed.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = "";

    // ============ Filter Properties ============

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private string _filterName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private string _filterId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private string _filterOwner = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private string? _filterCulture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private SettlementComponentType? _filterComponentType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilters))]
    private string? _filterReligion;

    /// <summary>
    /// Whether any filters are active.
    /// </summary>
    public bool HasFilters =>
        !string.IsNullOrEmpty(FilterName) ||
        !string.IsNullOrEmpty(FilterId) ||
        !string.IsNullOrEmpty(FilterOwner) ||
        !string.IsNullOrEmpty(FilterCulture) ||
        FilterComponentType.HasValue ||
        !string.IsNullOrEmpty(FilterReligion);

    // ============ Statistics ============

    /// <summary>
    /// Total number of settlements.
    /// </summary>
    public int TotalCount => AllSettlements.Count;

    /// <summary>
    /// Number of filtered settlements.
    /// </summary>
    public int FilteredCount => FilteredSettlements.Count;

    /// <summary>
    /// Number of selected settlements.
    /// </summary>
    public int SelectedCount => SelectedIds.Count;

    // ============ Events ============

    /// <summary>
    /// Event raised when selection changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Event raised when filters change and filtering is applied.
    /// </summary>
    public event EventHandler? FiltersApplied;

    /// <summary>
    /// Event raised when a settlement is modified.
    /// </summary>
    public event EventHandler<SettlementEntry>? SettlementModified;

    // ============ Map State ============

    /// <summary>
    /// Map bounds calculated from settlement positions.
    /// </summary>
    public (double minX, double maxX, double minY, double maxY) MapBounds { get; private set; }

    public SettlementEditContext(SettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    /// <summary>
    /// Loads settlements from the specified file path.
    /// </summary>
    public async Task LoadAsync(string filePath)
    {
        IsLoading = true;
        ErrorMessage = "";
        FilePath = filePath;

        try
        {
            await Task.Run(() => _settlementService.Load(filePath));

            AllSettlements.Clear();
            foreach (var settlement in _settlementService.Settlements)
            {
                AllSettlements.Add(settlement);
            }

            MapBounds = _settlementService.GetMapBounds();

            ApplyFilters();

            OnPropertyChanged(nameof(TotalCount));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load settlements: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Applies current filter criteria to update FilteredSettlements.
    /// </summary>
    public void ApplyFilters()
    {
        FilteredSettlements.Clear();

        foreach (var settlement in AllSettlements)
        {
            if (MatchesFilters(settlement))
            {
                FilteredSettlements.Add(settlement);
            }
        }

        OnPropertyChanged(nameof(FilteredCount));
        FiltersApplied?.Invoke(this, EventArgs.Empty);
    }

    private bool MatchesFilters(SettlementEntry settlement)
    {
        // Name filter
        if (!string.IsNullOrEmpty(FilterName))
        {
            if (!settlement.Name.Contains(FilterName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // ID filter
        if (!string.IsNullOrEmpty(FilterId))
        {
            if (!settlement.Id.Contains(FilterId, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Owner filter
        if (!string.IsNullOrEmpty(FilterOwner))
        {
            var matchesOwner = settlement.Owner.Contains(FilterOwner, StringComparison.OrdinalIgnoreCase) ||
                               settlement.OwnerDisplayName.Contains(FilterOwner, StringComparison.OrdinalIgnoreCase);
            if (!matchesOwner) return false;
        }

        // Culture filter
        if (!string.IsNullOrEmpty(FilterCulture))
        {
            if (!settlement.Culture.Equals(FilterCulture, StringComparison.OrdinalIgnoreCase) &&
                !settlement.Culture.Equals($"Culture.{FilterCulture}", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Component type filter
        if (FilterComponentType.HasValue)
        {
            if (settlement.ComponentType != FilterComponentType.Value)
                return false;
        }

        // Religion filter (for shrines)
        if (!string.IsNullOrEmpty(FilterReligion))
        {
            if (string.IsNullOrEmpty(settlement.Religion) ||
                !settlement.Religion.Contains(FilterReligion, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Clears all filters.
    /// </summary>
    public void ClearFilters()
    {
        FilterName = "";
        FilterId = "";
        FilterOwner = "";
        FilterCulture = null;
        FilterComponentType = null;
        FilterReligion = null;

        ApplyFilters();
    }

    /// <summary>
    /// Toggles selection of a settlement.
    /// </summary>
    public void ToggleSelection(string settlementId)
    {
        if (SelectedIds.Contains(settlementId))
        {
            SelectedIds.Remove(settlementId);
        }
        else
        {
            SelectedIds.Add(settlementId);
        }

        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets selection to a single settlement.
    /// </summary>
    public void SelectSingle(string settlementId)
    {
        SelectedIds.Clear();
        SelectedIds.Add(settlementId);

        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all selections.
    /// </summary>
    public void ClearSelection()
    {
        SelectedIds.Clear();
        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects all filtered settlements.
    /// </summary>
    public void SelectAllFiltered()
    {
        SelectedIds.Clear();
        foreach (var settlement in FilteredSettlements)
        {
            SelectedIds.Add(settlement.Id);
        }

        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets whether a settlement is selected.
    /// </summary>
    public bool IsSelected(string settlementId) => SelectedIds.Contains(settlementId);

    /// <summary>
    /// Gets selected settlements.
    /// </summary>
    public IEnumerable<SettlementEntry> GetSelectedSettlements()
    {
        return AllSettlements.Where(s => SelectedIds.Contains(s.Id));
    }

    /// <summary>
    /// Updates a settlement attribute.
    /// </summary>
    public void UpdateSettlement(SettlementEntry settlement, string attributeName, string value)
    {
        _settlementService.UpdateAttribute(settlement, attributeName, value);
        SettlementModified?.Invoke(this, settlement);
    }

    /// <summary>
    /// Updates a settlement location scene.
    /// </summary>
    public void UpdateLocationScene(SettlementEntry settlement, string locationId, string sceneAttribute, string value)
    {
        _settlementService.UpdateLocationScene(settlement, locationId, sceneAttribute, value);
        SettlementModified?.Invoke(this, settlement);
    }

    /// <summary>
    /// Saves changes to the file.
    /// </summary>
    public async Task SaveAsync()
    {
        await Task.Run(() => _settlementService.Save());
    }

    /// <summary>
    /// Gets whether there are unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges => _settlementService.HasUnsavedChanges;

    /// <summary>
    /// Gets all unique component types.
    /// </summary>
    public IEnumerable<SettlementComponentType> GetComponentTypes() => _settlementService.GetComponentTypes();

    /// <summary>
    /// Gets all unique cultures.
    /// </summary>
    public IEnumerable<string> GetCultures() => _settlementService.GetCultures();

    /// <summary>
    /// Gets all unique religions.
    /// </summary>
    public IEnumerable<string> GetReligions() => _settlementService.GetReligions();
}
