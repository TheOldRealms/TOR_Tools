using Avalonia.Controls;
using Avalonia.Input.Platform;
using TORTools.App.Controls;
using TORTools.App.ViewModels.Settlement;

namespace TORTools.App.Views.Settlement;

public partial class SettlementMapTabView : UserControl
{
    private SettlementMapTabViewModel? _viewModel;

    public SettlementMapTabView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.MapInvalidated -= OnMapInvalidated;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SettlementMapTabViewModel;

        if (_viewModel != null)
        {
            _viewModel.MapInvalidated += OnMapInvalidated;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Wire up map control events
            var mapControl = this.FindControl<SettlementMapControl>("MapControl");
            if (mapControl != null)
            {
                mapControl.SettlementClicked += OnSettlementClicked;
                mapControl.SettlementHovered += OnSettlementHovered;
                mapControl.HoverCleared += OnHoverCleared;
                mapControl.MapClicked += OnMapClicked;
                mapControl.MouseMoved += OnMouseMoved;

                // Set map image path - go up from bin/Debug/net10.0 to TORTools module root
                var mapImagePath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "tor_worldmap_6k_settlement_editor.png");
                mapImagePath = System.IO.Path.GetFullPath(mapImagePath);

                Console.WriteLine($"[SettlementMap] Looking for map at: {mapImagePath}");

                if (System.IO.File.Exists(mapImagePath))
                {
                    Console.WriteLine($"[SettlementMap] Setting map image path: {mapImagePath}");
                    mapControl.MapImagePath = mapImagePath;
                }
                else
                {
                    Console.WriteLine($"[SettlementMap] Map image NOT FOUND at: {mapImagePath}");
                }

                // Debug: log settlement count and sample coordinates
                if (_viewModel != null)
                {
                    var settlements = _viewModel.Context.FilteredSettlements;
                    Console.WriteLine($"[SettlementMap] Loaded {settlements.Count} settlements");
                    foreach (var s in settlements.Take(3))
                    {
                        Console.WriteLine($"[SettlementMap]   - {s.Id}: PosX={s.PosX}, PosY={s.PosY}");
                    }
                    var bounds = _viewModel.Context.MapBounds;
                    Console.WriteLine($"[SettlementMap] Context MapBounds: X={bounds.minX}-{bounds.maxX}, Y={bounds.minY}-{bounds.maxY}");
                }
            }
        }
    }

    private void OnMapInvalidated(object? sender, EventArgs e)
    {
        var mapControl = this.FindControl<SettlementMapControl>("MapControl");
        mapControl?.InvalidateVisual();
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle clipboard copy when CopiedCoordinates changes
        if (e.PropertyName == nameof(SettlementMapTabViewModel.CopiedCoordinates) && _viewModel != null)
        {
            var coords = _viewModel.CopiedCoordinates;
            if (!string.IsNullOrEmpty(coords))
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard != null)
                {
                    try
                    {
                        await topLevel.Clipboard.SetTextAsync(coords);
                    }
                    catch
                    {
                        // Clipboard operation failed
                    }
                }
            }
        }
    }

    private void OnSettlementClicked(object? sender, Core.Models.Settlement.SettlementEntry settlement)
    {
        _viewModel?.OnSettlementClick(settlement, false);
    }

    private void OnSettlementHovered(object? sender, Core.Models.Settlement.SettlementEntry settlement)
    {
        _viewModel?.SetHoveredSettlement(settlement);
    }

    private void OnHoverCleared(object? sender, EventArgs e)
    {
        _viewModel?.SetHoveredSettlement(null);
    }

    private void OnMapClicked(object? sender, (double X, double Y) worldPos)
    {
        _viewModel?.OnMapClick(worldPos.X, worldPos.Y, false);
    }

    private void OnMouseMoved(object? sender, (double X, double Y) worldPos)
    {
        _viewModel?.UpdateMousePosition(worldPos.X, worldPos.Y);
    }
}
