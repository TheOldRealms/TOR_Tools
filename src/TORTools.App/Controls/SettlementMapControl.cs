using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TORTools.Core.Models.Settlement;

namespace TORTools.App.Controls;

/// <summary>
/// Custom control for rendering the settlement map.
/// Displays a world map image with settlement markers overlaid.
/// </summary>
public class SettlementMapControl : Control
{
    // ============ Styled Properties ============

    public static readonly StyledProperty<IEnumerable<SettlementEntry>?> SettlementsProperty =
        AvaloniaProperty.Register<SettlementMapControl, IEnumerable<SettlementEntry>?>(nameof(Settlements));

    public static readonly StyledProperty<HashSet<string>?> SelectedIdsProperty =
        AvaloniaProperty.Register<SettlementMapControl, HashSet<string>?>(nameof(SelectedIds));

    public static readonly StyledProperty<SettlementEntry?> HoveredSettlementProperty =
        AvaloniaProperty.Register<SettlementMapControl, SettlementEntry?>(nameof(HoveredSettlement));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<SettlementMapControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<double> PanOffsetXProperty =
        AvaloniaProperty.Register<SettlementMapControl, double>(nameof(PanOffsetX), 0.0);

    public static readonly StyledProperty<double> PanOffsetYProperty =
        AvaloniaProperty.Register<SettlementMapControl, double>(nameof(PanOffsetY), 0.0);

    public static readonly StyledProperty<(double X, double Y)?> PointMarkerPositionProperty =
        AvaloniaProperty.Register<SettlementMapControl, (double X, double Y)?>(nameof(PointMarkerPosition));

    public static readonly StyledProperty<bool> IsPlacingPointMarkerProperty =
        AvaloniaProperty.Register<SettlementMapControl, bool>(nameof(IsPlacingPointMarker));

    public static readonly StyledProperty<string?> MapImagePathProperty =
        AvaloniaProperty.Register<SettlementMapControl, string?>(nameof(MapImagePath));

    public static readonly StyledProperty<(double minX, double maxX, double minY, double maxY)> MapBoundsProperty =
        AvaloniaProperty.Register<SettlementMapControl, (double minX, double maxX, double minY, double maxY)>(
            nameof(MapBounds), (0, 2070, 0, 2070));

    // ============ Properties ============

    public IEnumerable<SettlementEntry>? Settlements
    {
        get => GetValue(SettlementsProperty);
        set => SetValue(SettlementsProperty, value);
    }

    public HashSet<string>? SelectedIds
    {
        get => GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    public SettlementEntry? HoveredSettlement
    {
        get => GetValue(HoveredSettlementProperty);
        set => SetValue(HoveredSettlementProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double PanOffsetX
    {
        get => GetValue(PanOffsetXProperty);
        set => SetValue(PanOffsetXProperty, value);
    }

    public double PanOffsetY
    {
        get => GetValue(PanOffsetYProperty);
        set => SetValue(PanOffsetYProperty, value);
    }

    public (double X, double Y)? PointMarkerPosition
    {
        get => GetValue(PointMarkerPositionProperty);
        set => SetValue(PointMarkerPositionProperty, value);
    }

    public bool IsPlacingPointMarker
    {
        get => GetValue(IsPlacingPointMarkerProperty);
        set => SetValue(IsPlacingPointMarkerProperty, value);
    }

    public string? MapImagePath
    {
        get => GetValue(MapImagePathProperty);
        set => SetValue(MapImagePathProperty, value);
    }

    public (double minX, double maxX, double minY, double maxY) MapBounds
    {
        get => GetValue(MapBoundsProperty);
        set => SetValue(MapBoundsProperty, value);
    }

    // ============ Events ============

    public event EventHandler<SettlementEntry>? SettlementClicked;
    public event EventHandler<SettlementEntry>? SettlementHovered;
    public event EventHandler? HoverCleared;
    public event EventHandler<(double X, double Y)>? MapClicked;
    public event EventHandler<(double X, double Y)>? MouseMoved;

    // ============ State ============

    private Bitmap? _mapImage;
    private bool _isPanning;
    private Point _lastPanPoint;

    // Map coordinate mapping - TOR world is 2070x2070 units
    private double _mapMinX = 0;
    private double _mapMaxX = 2070;
    private double _mapMinY = 0;
    private double _mapMaxY = 2070;

    // ============ Rendering ============

    static SettlementMapControl()
    {
        AffectsRender<SettlementMapControl>(
            SettlementsProperty,
            SelectedIdsProperty,
            HoveredSettlementProperty,
            ZoomLevelProperty,
            PanOffsetXProperty,
            PanOffsetYProperty,
            PointMarkerPositionProperty,
            MapImagePathProperty,
            MapBoundsProperty
        );
    }

    public SettlementMapControl()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MapImagePathProperty)
        {
            LoadMapImage(change.GetNewValue<string?>());
        }
        else if (change.Property == MapBoundsProperty)
        {
            var bounds = change.GetNewValue<(double minX, double maxX, double minY, double maxY)>();
            _mapMinX = bounds.minX;
            _mapMaxX = bounds.maxX;
            _mapMinY = bounds.minY;
            _mapMaxY = bounds.maxY;
            Console.WriteLine($"[SettlementMap] Map bounds set: X={_mapMinX}-{_mapMaxX}, Y={_mapMinY}-{_mapMaxY}");
        }
    }

    private void LoadMapImage(string? path)
    {
        _mapImage?.Dispose();
        _mapImage = null;

        Console.WriteLine($"[SettlementMap] LoadMapImage: {path}");

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                _mapImage = new Bitmap(path);
                Console.WriteLine($"[SettlementMap] Map image loaded: {_mapImage.Size.Width}x{_mapImage.Size.Height}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettlementMap] Failed to load map image: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[SettlementMap] Map image not found at path");
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Sets the world coordinate bounds for mapping.
    /// </summary>
    public void SetMapBounds(double minX, double maxX, double minY, double maxY)
    {
        _mapMinX = minX;
        _mapMaxX = maxX;
        _mapMinY = minY;
        _mapMaxY = maxY;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Draw background
        context.FillRectangle(Brushes.DarkSlateGray, new Rect(bounds.Size));

        // Draw map image if loaded
        if (_mapImage != null)
        {
            var imageRect = CalculateImageRect(bounds);
            context.DrawImage(_mapImage, imageRect);
        }

        // Draw settlements
        DrawSettlements(context, bounds);

        // Draw point marker
        DrawPointMarker(context, bounds);

        // Draw tooltip for hovered settlement
        DrawTooltip(context, bounds);
    }

    private Rect CalculateImageRect(Rect bounds)
    {
        if (_mapImage == null) return default;

        // Calculate the scaled and panned image rectangle
        var imageWidth = _mapImage.Size.Width * ZoomLevel;
        var imageHeight = _mapImage.Size.Height * ZoomLevel;

        // Center the image initially
        var x = (bounds.Width - imageWidth) / 2 + PanOffsetX;
        var y = (bounds.Height - imageHeight) / 2 + PanOffsetY;

        return new Rect(x, y, imageWidth, imageHeight);
    }

    private void DrawSettlements(DrawingContext context, Rect bounds)
    {
        if (Settlements == null) return;

        foreach (var settlement in Settlements)
        {
            var pos = WorldToCanvas(settlement.PosX, settlement.PosY, bounds);
            if (pos.X < -20 || pos.X > bounds.Width + 20 ||
                pos.Y < -20 || pos.Y > bounds.Height + 20)
            {
                continue; // Off screen
            }

            var isSelected = SelectedIds?.Contains(settlement.Id) ?? false;
            var isHovered = HoveredSettlement?.Id == settlement.Id;

            DrawSettlementMarker(context, settlement, pos, isSelected, isHovered);
        }
    }

    private void DrawSettlementMarker(DrawingContext context, SettlementEntry settlement, Point pos, bool isSelected, bool isHovered)
    {
        var baseSize = 8.0 * ZoomLevel;
        var size = isHovered ? baseSize * 1.5 : baseSize;

        // Get color based on component type
        var fillBrush = GetSettlementBrush(settlement.ComponentType);
        var strokeBrush = isSelected ? Brushes.Yellow : (isHovered ? Brushes.White : Brushes.Black);
        var strokeThickness = isSelected ? 3.0 : (isHovered ? 2.0 : 1.0);
        var pen = new Pen(strokeBrush, strokeThickness);

        // Draw shape based on component type
        switch (settlement.ComponentType)
        {
            case SettlementComponentType.Shrine:
            case SettlementComponentType.OakOfAges:
            case SettlementComponentType.WorldRoots:
            case SettlementComponentType.ChaosPortal:
            case SettlementComponentType.SlaverCamp:
            case SettlementComponentType.Town:
                // Circle
                context.DrawEllipse(fillBrush, pen, pos, size, size);
                break;

            case SettlementComponentType.Castle:
                // Square
                var squareSize = size * 1.5;
                var squareRect = new Rect(pos.X - squareSize / 2, pos.Y - squareSize / 2, squareSize, squareSize);
                context.DrawRectangle(fillBrush, pen, squareRect);
                break;

            case SettlementComponentType.HerdStone:
            case SettlementComponentType.Village:
            case SettlementComponentType.Hideout:
                // Triangle (using path geometry)
                var triangleSize = size * 1.2;
                var geometry = CreateTriangleGeometry(pos, triangleSize);
                context.DrawGeometry(fillBrush, pen, geometry);
                break;

            default:
                // Default circle
                context.DrawEllipse(Brushes.Gray, pen, pos, size, size);
                break;
        }
    }

    private static Geometry CreateTriangleGeometry(Point center, double size)
    {
        var top = new Point(center.X, center.Y - size);
        var bottomLeft = new Point(center.X - size * 0.866, center.Y + size * 0.5);
        var bottomRight = new Point(center.X + size * 0.866, center.Y + size * 0.5);

        var figure = new PathFigure
        {
            StartPoint = top,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments?.Add(new LineSegment { Point = bottomLeft });
        figure.Segments?.Add(new LineSegment { Point = bottomRight });

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures?.Add(figure);
        return pathGeometry;
    }

    private static IBrush GetSettlementBrush(SettlementComponentType type)
    {
        return type switch
        {
            SettlementComponentType.Shrine => Brushes.MediumPurple,
            SettlementComponentType.HerdStone => Brushes.SaddleBrown,
            SettlementComponentType.OakOfAges => Brushes.ForestGreen,
            SettlementComponentType.WorldRoots => Brushes.DarkGreen,
            SettlementComponentType.ChaosPortal => Brushes.Crimson,
            SettlementComponentType.SlaverCamp => Brushes.DimGray,
            SettlementComponentType.Town => Brushes.DodgerBlue,
            SettlementComponentType.Castle => Brushes.Gold,
            SettlementComponentType.Village => Brushes.LimeGreen,
            SettlementComponentType.Hideout => Brushes.DarkOliveGreen,
            _ => Brushes.Gray
        };
    }

    private void DrawPointMarker(DrawingContext context, Rect bounds)
    {
        if (!PointMarkerPosition.HasValue) return;

        var pos = WorldToCanvas(PointMarkerPosition.Value.X, PointMarkerPosition.Value.Y, bounds);

        // Draw crosshair
        var crossSize = 15.0;
        var pen = new Pen(Brushes.Orange, 2);
        context.DrawLine(pen, new Point(pos.X - crossSize, pos.Y), new Point(pos.X + crossSize, pos.Y));
        context.DrawLine(pen, new Point(pos.X, pos.Y - crossSize), new Point(pos.X, pos.Y + crossSize));

        // Draw circle
        context.DrawEllipse(null, pen, pos, 8, 8);
    }

    private void DrawTooltip(DrawingContext context, Rect bounds)
    {
        if (HoveredSettlement == null) return;

        var pos = WorldToCanvas(HoveredSettlement.PosX, HoveredSettlement.PosY, bounds);

        // Tooltip text
        var text = $"{HoveredSettlement.Name}\n{HoveredSettlement.ComponentTypeDisplay}";
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            Brushes.White);

        // Position tooltip above the marker
        var tooltipX = pos.X - formattedText.Width / 2;
        var tooltipY = pos.Y - 35 - formattedText.Height;

        // Keep tooltip on screen
        tooltipX = Math.Max(5, Math.Min(tooltipX, bounds.Width - formattedText.Width - 5));
        tooltipY = Math.Max(5, tooltipY);

        // Draw background
        var padding = 5.0;
        var bgRect = new Rect(
            tooltipX - padding,
            tooltipY - padding,
            formattedText.Width + padding * 2,
            formattedText.Height + padding * 2);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)), bgRect, 3);

        // Draw text
        context.DrawText(formattedText, new Point(tooltipX, tooltipY));
    }

    // ============ Coordinate Mapping ============

    private Point WorldToCanvas(double worldX, double worldY, Rect bounds)
    {
        // Normalize world coordinates to 0-1 range based on map bounds
        var rangeX = _mapMaxX - _mapMinX;
        var rangeY = _mapMaxY - _mapMinY;

        if (rangeX <= 0) rangeX = 1;
        if (rangeY <= 0) rangeY = 1;

        var normalizedX = (worldX - _mapMinX) / rangeX;
        var normalizedY = (worldY - _mapMinY) / rangeY;

        if (_mapImage != null)
        {
            // Map to image coordinates (flipping Y since image origin is top-left)
            var imageX = normalizedX * _mapImage.Size.Width;
            var imageY = (1 - normalizedY) * _mapImage.Size.Height;

            // Apply zoom and pan
            var imageRect = CalculateImageRect(bounds);
            var canvasX = imageRect.X + imageX * ZoomLevel;
            var canvasY = imageRect.Y + imageY * ZoomLevel;

            return new Point(canvasX, canvasY);
        }
        else
        {
            // No map image - just map to canvas bounds directly
            var canvasX = normalizedX * bounds.Width * ZoomLevel + PanOffsetX;
            var canvasY = (1 - normalizedY) * bounds.Height * ZoomLevel + PanOffsetY;

            return new Point(canvasX, canvasY);
        }
    }

    private (double X, double Y) CanvasToWorld(Point canvasPos, Rect bounds)
    {
        if (_mapImage == null) return (0, 0);

        var imageRect = CalculateImageRect(bounds);

        // Convert canvas to image coordinates
        var imageX = (canvasPos.X - imageRect.X) / ZoomLevel;
        var imageY = (canvasPos.Y - imageRect.Y) / ZoomLevel;

        // Normalize to 0-1 range
        var normalizedX = imageX / _mapImage.Size.Width;
        var normalizedY = 1 - (imageY / _mapImage.Size.Height); // Flip Y

        // Map to world coordinates
        var worldX = _mapMinX + normalizedX * (_mapMaxX - _mapMinX);
        var worldY = _mapMinY + normalizedY * (_mapMaxY - _mapMinY);

        return (worldX, worldY);
    }

    // ============ Input Handling ============

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        var worldPos = CanvasToWorld(pos, Bounds);

        // Handle panning
        if (_isPanning)
        {
            var delta = pos - _lastPanPoint;
            PanOffsetX += delta.X;
            PanOffsetY += delta.Y;
            _lastPanPoint = pos;
            InvalidateVisual();
            return;
        }

        // Notify mouse position
        MouseMoved?.Invoke(this, worldPos);

        // Check for settlement hover
        var hoveredSettlement = FindSettlementAtPoint(pos);
        if (hoveredSettlement != HoveredSettlement)
        {
            if (hoveredSettlement != null)
            {
                SettlementHovered?.Invoke(this, hoveredSettlement);
            }
            else
            {
                HoverCleared?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;

        // Middle mouse or Shift+Left for panning
        if (point.Properties.IsMiddleButtonPressed ||
            (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _isPanning = true;
            _lastPanPoint = pos;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        // Left click
        if (point.Properties.IsLeftButtonPressed)
        {
            var settlement = FindSettlementAtPoint(pos);
            var isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (settlement != null)
            {
                SettlementClicked?.Invoke(this, settlement);
            }
            else
            {
                var worldPos = CanvasToWorld(pos, Bounds);
                MapClicked?.Invoke(this, worldPos);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta.Y;
        var zoomFactor = delta > 0 ? 1.1 : 0.9;

        // Get mouse position for zoom center
        var mousePos = e.GetPosition(this);

        // Adjust pan to zoom around mouse position
        var oldZoom = ZoomLevel;
        var newZoom = Math.Clamp(ZoomLevel * zoomFactor, 0.1, 10.0);

        if (Math.Abs(newZoom - oldZoom) > 0.001)
        {
            var zoomRatio = newZoom / oldZoom;

            // Adjust pan to keep mouse position stable
            var dx = mousePos.X - Bounds.Width / 2;
            var dy = mousePos.Y - Bounds.Height / 2;
            PanOffsetX = (PanOffsetX - dx) * zoomRatio + dx;
            PanOffsetY = (PanOffsetY - dy) * zoomRatio + dy;

            ZoomLevel = newZoom;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoverCleared?.Invoke(this, EventArgs.Empty);
    }

    private SettlementEntry? FindSettlementAtPoint(Point canvasPoint)
    {
        if (Settlements == null) return null;

        var hitRadius = 15.0 * ZoomLevel;

        foreach (var settlement in Settlements)
        {
            var pos = WorldToCanvas(settlement.PosX, settlement.PosY, Bounds);
            var distance = Math.Sqrt(
                Math.Pow(canvasPoint.X - pos.X, 2) +
                Math.Pow(canvasPoint.Y - pos.Y, 2));

            if (distance <= hitRadius)
            {
                return settlement;
            }
        }

        return null;
    }
}
