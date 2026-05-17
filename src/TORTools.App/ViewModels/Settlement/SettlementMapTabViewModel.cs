using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TORTools.Core.Models.Settlement;
using TORTools.Core.Services.Settlement;

namespace TORTools.App.ViewModels.Settlement;

/// <summary>
/// ViewModel for the Settlement Map View tab.
/// Displays settlements on the world map with zoom/pan and click-to-edit functionality.
/// </summary>
public partial class SettlementMapTabViewModel : ViewModelBase
{
    private readonly SettlementEditContext _context;

    /// <summary>
    /// Tab title displayed in the tab strip.
    /// </summary>
    public string Title => "Settlement Map";

    /// <summary>
    /// Full title including file info.
    /// </summary>
    public string FullTitle => $"Settlement Map - {System.IO.Path.GetFileName(_context.FilePath)}";

    /// <summary>
    /// The shared edit context.
    /// </summary>
    public SettlementEditContext Context => _context;

    // ============ Map State ============

    /// <summary>
    /// Current zoom level (1.0 = 100%).
    /// </summary>
    [ObservableProperty]
    private double _zoomLevel = 1.0;

    /// <summary>
    /// Pan offset X (in canvas coordinates).
    /// </summary>
    [ObservableProperty]
    private double _panOffsetX;

    /// <summary>
    /// Pan offset Y (in canvas coordinates).
    /// </summary>
    [ObservableProperty]
    private double _panOffsetY;

    /// <summary>
    /// Current mouse position in world coordinates (for status bar).
    /// </summary>
    [ObservableProperty]
    private double _mouseWorldX;

    /// <summary>
    /// Current mouse position in world coordinates (for status bar).
    /// </summary>
    [ObservableProperty]
    private double _mouseWorldY;

    /// <summary>
    /// Formatted zoom percentage for display.
    /// </summary>
    public string ZoomPercentage => $"{ZoomLevel * 100:F0}%";

    /// <summary>
    /// Formatted mouse coordinates for status bar.
    /// </summary>
    public string MouseCoordsText => $"X: {MouseWorldX:F1}, Y: {MouseWorldY:F1}";

    // ============ Point Marker State ============

    /// <summary>
    /// Whether point marker placement mode is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointMarkerModeText))]
    private bool _isPlacingPointMarker;

    /// <summary>
    /// Current point marker position (null if not placed).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPointMarker))]
    [NotifyPropertyChangedFor(nameof(PointMarkerText))]
    private (double X, double Y)? _pointMarkerPosition;

    /// <summary>
    /// Whether a point marker is currently placed.
    /// </summary>
    public bool HasPointMarker => PointMarkerPosition.HasValue;

    /// <summary>
    /// Point marker button text.
    /// </summary>
    public string PointMarkerModeText => IsPlacingPointMarker ? "Click on Map..." : "Place Marker";

    /// <summary>
    /// Point marker coordinates text.
    /// </summary>
    public string PointMarkerText => PointMarkerPosition.HasValue
        ? $"X: {PointMarkerPosition.Value.X:F3}, Y: {PointMarkerPosition.Value.Y:F3}"
        : "No marker placed";

    // ============ Status ============

    /// <summary>
    /// Status bar text.
    /// </summary>
    public string StatusText => $"{Context.FilteredCount} settlements shown | {Context.SelectedCount} selected";

    /// <summary>
    /// Whether there are unsaved changes.
    /// </summary>
    public bool HasUnsavedChanges => Context.HasUnsavedChanges;

    // ============ Events ============

    /// <summary>
    /// Event raised when the map needs to be redrawn.
    /// </summary>
    public event EventHandler? MapInvalidated;

    /// <summary>
    /// Event raised when a settlement is clicked (to open popup editor).
    /// </summary>
    public event EventHandler<SettlementEntry>? SettlementClicked;

    public SettlementMapTabViewModel(SettlementEditContext context)
    {
        _context = context;

        // Subscribe to context events
        _context.SelectionChanged += OnSelectionChanged;
        _context.FiltersApplied += OnFiltersApplied;
        _context.PropertyChanged += OnContextPropertyChanged;
    }

    /// <summary>
    /// Loads the settlement data.
    /// </summary>
    public async Task LoadAsync(string filePath)
    {
        await _context.LoadAsync(filePath);
        FitMapToContent();
        InvalidateMap();
    }

    /// <summary>
    /// Zooms the map in.
    /// </summary>
    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.25, 10.0);
        OnPropertyChanged(nameof(ZoomPercentage));
        InvalidateMap();
    }

    /// <summary>
    /// Zooms the map out.
    /// </summary>
    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.25, 0.1);
        OnPropertyChanged(nameof(ZoomPercentage));
        InvalidateMap();
    }

    /// <summary>
    /// Resets zoom to 100%.
    /// </summary>
    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
        OnPropertyChanged(nameof(ZoomPercentage));
        InvalidateMap();
    }

    /// <summary>
    /// Fits the map to show all settlements.
    /// </summary>
    [RelayCommand]
    private void FitMapToContent()
    {
        // Reset to initial view
        ZoomLevel = 1.0;
        PanOffsetX = 0;
        PanOffsetY = 0;
        OnPropertyChanged(nameof(ZoomPercentage));
        InvalidateMap();
    }

    /// <summary>
    /// Toggles point marker placement mode.
    /// </summary>
    [RelayCommand]
    private void TogglePointMarkerMode()
    {
        IsPlacingPointMarker = !IsPlacingPointMarker;
        if (!IsPlacingPointMarker)
        {
            // Exiting placement mode without placing
        }
    }

    /// <summary>
    /// Places a point marker at the specified world coordinates.
    /// </summary>
    public void PlacePointMarker(double worldX, double worldY)
    {
        PointMarkerPosition = (worldX, worldY);
        IsPlacingPointMarker = false;
        InvalidateMap();
    }

    /// <summary>
    /// Clears the current point marker.
    /// </summary>
    [RelayCommand]
    private void ClearPointMarker()
    {
        PointMarkerPosition = null;
        InvalidateMap();
    }

    /// <summary>
    /// Copies point marker coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyPointMarkerCoords()
    {
        if (!PointMarkerPosition.HasValue) return;

        var coords = $"posX=\"{PointMarkerPosition.Value.X:F3}\" posY=\"{PointMarkerPosition.Value.Y:F3}\"";
        CopiedCoordinates = coords;

        // Note: Actual clipboard copy will be done in the View via TopLevel.GetTopLevel().Clipboard
        // The ViewModel just stores the value to copy
    }

    /// <summary>
    /// Coordinates to copy to clipboard. The View should watch this and do the actual clipboard operation.
    /// </summary>
    [ObservableProperty]
    private string? _copiedCoordinates;

    /// <summary>
    /// Clears all filters.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        _context.ClearFilters();
    }

    /// <summary>
    /// Clears selection.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        _context.ClearSelection();
    }

    /// <summary>
    /// Saves changes.
    /// </summary>
    [RelayCommand]
    private async Task Save()
    {
        await _context.SaveAsync();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>
    /// Called when mouse moves over the map (to update coords display).
    /// </summary>
    public void UpdateMousePosition(double worldX, double worldY)
    {
        MouseWorldX = worldX;
        MouseWorldY = worldY;
        OnPropertyChanged(nameof(MouseCoordsText));
    }

    /// <summary>
    /// Called when a settlement is hovered.
    /// </summary>
    public void SetHoveredSettlement(SettlementEntry? settlement)
    {
        _context.HoveredSettlement = settlement;
        InvalidateMap();
    }

    /// <summary>
    /// Called when a settlement is clicked.
    /// </summary>
    public void OnSettlementClick(SettlementEntry settlement, bool isCtrlPressed)
    {
        if (isCtrlPressed)
        {
            // Ctrl+click toggles selection
            _context.ToggleSelection(settlement.Id);
        }
        else
        {
            // Regular click selects single and opens popup
            _context.SelectSingle(settlement.Id);
            SettlementClicked?.Invoke(this, settlement);
        }

        InvalidateMap();
    }

    /// <summary>
    /// Called when clicking on empty map space.
    /// </summary>
    public void OnMapClick(double worldX, double worldY, bool isCtrlPressed)
    {
        if (IsPlacingPointMarker)
        {
            PlacePointMarker(worldX, worldY);
        }
        else if (!isCtrlPressed)
        {
            _context.ClearSelection();
            InvalidateMap();
        }
    }

    /// <summary>
    /// Forces the map to redraw.
    /// </summary>
    public void InvalidateMap()
    {
        MapInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        InvalidateMap();
    }

    private void OnFiltersApplied(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        InvalidateMap();
    }

    private void OnContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettlementEditContext.HoveredSettlement))
        {
            InvalidateMap();
        }
    }

    /// <summary>
    /// Cleanup.
    /// </summary>
    public void Dispose()
    {
        _context.SelectionChanged -= OnSelectionChanged;
        _context.FiltersApplied -= OnFiltersApplied;
        _context.PropertyChanged -= OnContextPropertyChanged;
    }
}
