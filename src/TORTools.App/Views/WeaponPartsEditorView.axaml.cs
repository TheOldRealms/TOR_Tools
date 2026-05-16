using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TORTools.App.Controls;
using TORTools.App.ViewModels;
using TORTools.Core.Services;

namespace TORTools.App.Views;

public partial class WeaponPartsEditorView : Window
{
    private WeaponPartsEditorViewModel? _viewModel;
    private OpenGLViewport? _viewport;

    // Mouse interaction state for viewport
    private bool _isRotating;
    private bool _isPanning;
    private Point _lastMousePos;

    /// <summary>
    /// Result indicating whether the user applied changes.
    /// </summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// The selected pieces after Apply.
    /// </summary>
    public (string? bladeId, string? handleId, string? guardId, string? pommelId,
        int bladeScale, int handleScale, int guardScale, int pommelScale)? Selection { get; private set; }

    public WeaponPartsEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the editor with the required services and paths.
    /// </summary>
    public void Initialize(
        CraftingPieceCatalogService catalogService,
        FbxLoaderService fbxLoaderService,
        string moduleDataPath,
        string assetSourcesPath)
    {
        _viewModel = new WeaponPartsEditorViewModel(catalogService, fbxLoaderService);
        DataContext = _viewModel;

        // Get viewport reference
        _viewport = this.FindControl<OpenGLViewport>("Viewport");

        // Subscribe to mesh changes
        _viewModel.MeshesChanged += OnMeshesChanged;
        _viewModel.PieceHighlighted += OnPieceHighlighted;

        // Initialize the view model
        _viewModel.Initialize(moduleDataPath, assetSourcesPath);
    }

    /// <summary>
    /// Sets initial piece selections for editing an existing weapon.
    /// </summary>
    public void SetInitialSelection(
        string? templateId,
        string? bladeId, string? handleId, string? guardId, string? pommelId,
        int bladeScale = 100, int handleScale = 100, int guardScale = 100, int pommelScale = 100)
    {
        Console.WriteLine($"[WeaponPartsEditor] SetInitialSelection called");
        Console.WriteLine($"[WeaponPartsEditor] templateId: {templateId ?? "(null)"}");
        Console.WriteLine($"[WeaponPartsEditor] bladeId: {bladeId ?? "(null)"}, handleId: {handleId ?? "(null)"}");

        if (_viewModel == null)
        {
            Console.WriteLine($"[WeaponPartsEditor] ERROR: _viewModel is null!");
            return;
        }

        Console.WriteLine($"[WeaponPartsEditor] Available templates count: {_viewModel.AvailableTemplates.Count}");

        // Find and select the template
        if (!string.IsNullOrEmpty(templateId))
        {
            var template = _viewModel.AvailableTemplates.FirstOrDefault(t => t.Id == templateId);
            Console.WriteLine($"[WeaponPartsEditor] Found matching template: {template != null}");

            if (template != null)
            {
                _viewModel.SelectedTemplate = template;

                // Wait a moment for piece lists to populate, then set selections
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Console.WriteLine($"[WeaponPartsEditor] Setting initial pieces...");
                    _viewModel.SetInitialPieces(bladeId, handleId, guardId, pommelId,
                        bladeScale, handleScale, guardScale, pommelScale);
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                Console.WriteLine($"[WeaponPartsEditor] Template '{templateId}' not found in available templates");
                // List first few templates for debugging
                foreach (var t in _viewModel.AvailableTemplates.Take(5))
                {
                    Console.WriteLine($"[WeaponPartsEditor]   Available: {t.Id}");
                }
            }
        }
        else
        {
            Console.WriteLine($"[WeaponPartsEditor] No template ID provided");
        }
    }

    private void OnMeshesChanged(bool fitCamera)
    {
        if (_viewport == null || _viewModel == null) return;

        _viewport.ClearMeshes();

        foreach (var (mesh, offset, scale) in _viewModel.LoadedMeshes)
        {
            _viewport.AddMesh(mesh, offset, scale);
        }

        // Only fit camera when pieces change, not for offset/scale tweaks
        if (fitCamera)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _viewport.FitToContent();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OnPieceHighlighted(CraftingPieceInfo? piece)
    {
        if (_viewport == null || _viewModel == null) return;

        // Find the mesh for this piece
        MeshData? meshToHighlight = null;
        if (piece != null)
        {
            foreach (var (mesh, _, _) in _viewModel.LoadedMeshes)
            {
                if (mesh.MeshName == piece.MeshName)
                {
                    meshToHighlight = mesh;
                    break;
                }
            }
        }

        _viewport.SetHighlightedMesh(meshToHighlight);
    }

    private void OnResetCamera(object? sender, RoutedEventArgs e)
    {
        _viewport?.ResetCamera();
    }

    private void OnFitToView(object? sender, RoutedEventArgs e)
    {
        _viewport?.FitToContent();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            // Save modified offsets to XML
            var savedCount = _viewModel.SaveModifiedOffsets();
            if (savedCount > 0)
            {
                Console.WriteLine($"[WeaponPartsEditor] Saved {savedCount} piece offset(s) to XML.");
            }
            else if (savedCount < 0)
            {
                Console.WriteLine("[WeaponPartsEditor] Error saving offsets.");
            }

            Selection = _viewModel.GetSelection();
        }
        DialogResult = true;
        Close();
    }

    // Viewport mouse event handlers (forwarded from overlay)
    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewport == null) return;

        var point = e.GetCurrentPoint(sender as Control);

        if (point.Properties.IsLeftButtonPressed)
        {
            _isRotating = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(sender as Control);
            e.Handled = true;
        }
        else if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _isPanning = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(sender as Control);
            e.Handled = true;
        }
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isRotating = false;
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewport == null) return;

        var point = e.GetCurrentPoint(sender as Control);
        var currentPos = point.Position;
        var delta = currentPos - _lastMousePos;
        _lastMousePos = currentPos;

        if (_isRotating)
        {
            _viewport.HandleRotate(delta.X, delta.Y);
        }
        else if (_isPanning)
        {
            _viewport.HandlePan(delta.X, delta.Y);
        }
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewport == null) return;

        _viewport.HandleZoom(e.Delta.Y);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Unsubscribe from events
        if (_viewModel != null)
        {
            _viewModel.MeshesChanged -= OnMeshesChanged;
            _viewModel.PieceHighlighted -= OnPieceHighlighted;
        }
    }
}
