using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Fenestration.Core.Geometry;
using Fenestration.Core.Viewport;
using Fenestration.Designer.Rendering;

namespace Fenestration.Designer.ViewModels;

/// <summary>
/// The viewport's state and navigation operations: zoom, pan, grid/axes visibility, viewport size,
/// cursor position and the content layers to draw. All coordinate math is delegated to the Core
/// <see cref="ViewportTransform"/>; this class adds settings, change notification and commands.
/// It never touches domain objects, so navigation cannot modify the design.
/// </summary>
public class CanvasViewModel : ViewModelBase
{
    private const double WheelDeltaPerNotch = 120.0;

    private readonly ViewportTransform _transform = new();
    private bool _hasBeenSized;

    public CanvasViewModel(ViewportSettings? settings = null)
    {
        Settings = settings ?? new ViewportSettings();
        Settings.Validate();
        _transform.SetZoomLimits(Settings.MinZoom, Settings.MaxZoom);

        ContentLayers.CollectionChanged += (_, _) => InvalidateContent();

        ZoomInCommand = new RelayCommand(ZoomIn);
        ZoomOutCommand = new RelayCommand(ZoomOut);
        FitCommand = new RelayCommand(FitToContent);
        ResetViewCommand = new RelayCommand(ResetView);
    }

    public ViewportSettings Settings { get; }

    /// <summary>The underlying world ↔ screen transform (read by the renderer).</summary>
    public ViewportTransform Transform => _transform;

    /// <summary>Raised when anything affecting the whole view changes (zoom, pan, size, grid, axes).</summary>
    public event Action? ViewChanged;

    /// <summary>Raised when content layers change and only the content needs redrawing.</summary>
    public event Action? ContentChanged;

    // ── Commands ────────────────────────────────────────────────────

    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand FitCommand { get; }
    public ICommand ResetViewCommand { get; }

    // ── Zoom & pan state ────────────────────────────────────────────

    /// <summary>Pixels per millimetre. 1.0 = 1 mm per device-independent pixel ("100 %").</summary>
    public double ZoomLevel
    {
        get => _transform.Zoom;
        set
        {
            if (_transform.Zoom.Equals(value)) return;
            _transform.Zoom = value;
            RaiseViewChanged();
        }
    }

    /// <summary>World X (mm) shown at the left edge of the canvas.</summary>
    public double PanX
    {
        get => _transform.PanX;
        set
        {
            _transform.PanX = value;
            RaiseViewChanged();
        }
    }

    /// <summary>World Y (mm) shown at the top edge of the canvas.</summary>
    public double PanY
    {
        get => _transform.PanY;
        set
        {
            _transform.PanY = value;
            RaiseViewChanged();
        }
    }

    /// <summary>Zoom level formatted for display (e.g. "150%").</summary>
    public string ZoomPercentage => string.Create(CultureInfo.InvariantCulture, $"{ZoomLevel * 100.0:0}%");

    // ── Viewport size ───────────────────────────────────────────────

    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }

    /// <summary>
    /// Called by the view when its size changes. The first time a real size arrives, the view fits the
    /// content (or resets if there is none) so the initial picture is predictable.
    /// </summary>
    public void SetViewportSize(double width, double height)
    {
        GeometryValidation.EnsureNonNegative(width);
        GeometryValidation.EnsureNonNegative(height);
        if (width.Equals(ViewportWidth) && height.Equals(ViewportHeight)) return;

        ViewportWidth = width;
        ViewportHeight = height;

        if (!_hasBeenSized && width > 0 && height > 0)
        {
            _hasBeenSized = true;
            FitToContent();
            return;
        }
        RaiseViewChanged();
    }

    // ── Grid & axes ─────────────────────────────────────────────────

    private bool _showGrid = true;
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetProperty(ref _showGrid, value))
                ViewChanged?.Invoke();
        }
    }

    private bool _showAxes = true;
    public bool ShowAxes
    {
        get => _showAxes;
        set
        {
            if (SetProperty(ref _showAxes, value))
                ViewChanged?.Invoke();
        }
    }

    private double _gridSpacingMm = Core.Utilities.Units.DefaultGridSpacingMm;

    /// <summary>
    /// Snap-grid increment in mm, used by grid snapping in a later milestone. The DISPLAYED grid adapts to
    /// zoom (see <see cref="GridSpacing"/>) and is independent of this value.
    /// </summary>
    public double GridSpacingMm
    {
        get => _gridSpacingMm;
        set => SetProperty(ref _gridSpacingMm, value > 0 ? value : _gridSpacingMm);
    }

    private bool _snapToGrid;
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => SetProperty(ref _snapToGrid, value);
    }

    /// <summary>The displayed grid spacing at the current zoom.</summary>
    public GridSpacing CurrentGridSpacing => GridSpacing.ForZoom(ZoomLevel, Settings.MinGridPixelSpacing);

    // ── Content ─────────────────────────────────────────────────────

    /// <summary>World-space layers drawn above the grid, in order.</summary>
    public ObservableCollection<IViewportLayer> ContentLayers { get; } = new();

    /// <summary>Union of all layer bounds (empty if there is no content).</summary>
    public BoundingBox2D ContentBounds => BoundingBox2D.Union(ContentLayers.Select(l => l.Bounds));

    /// <summary>Requests a redraw of the content layers (e.g. after the model changed).</summary>
    public void InvalidateContent() => ContentChanged?.Invoke();

    /// <summary>
    /// Screen-feedback layers drawn above the content (selection box, handles, snap markers, previews).
    /// They are redrawn on their own, without re-rendering the design, and are not part of Fit to Screen.
    /// </summary>
    public ObservableCollection<IViewportLayer> OverlayLayers { get; } = new();

    /// <summary>Raised when only the overlay needs redrawing.</summary>
    public event Action? OverlayChanged;

    public void InvalidateOverlay() => OverlayChanged?.Invoke();

    // ── Cursor ──────────────────────────────────────────────────────

    private Point2D? _cursorWorldPosition;

    /// <summary>World position (mm) under the mouse, or null when the mouse is outside the viewport.</summary>
    public Point2D? CursorWorldPosition
    {
        get => _cursorWorldPosition;
        private set
        {
            if (SetProperty(ref _cursorWorldPosition, value))
                OnPropertyChanged(nameof(CursorText));
        }
    }

    /// <summary>Cursor position for the status bar, e.g. "X: 1250.4 mm   Y: 743.8 mm".</summary>
    public string CursorText => _cursorWorldPosition is { } p
        ? string.Create(CultureInfo.InvariantCulture, $"X: {p.X:0.0} mm   Y: {p.Y:0.0} mm")
        : "X: —   Y: —";

    /// <summary>Updates the cursor readout from a screen position, via <see cref="ViewportTransform.ScreenToWorld"/>.</summary>
    public void UpdateCursor(Point2D screenPosition)
        => CursorWorldPosition = _transform.ScreenToWorld(screenPosition);

    public void ClearCursor() => CursorWorldPosition = null;

    // ── Coordinate transforms ───────────────────────────────────────

    public Point2D WorldToScreen(Point2D world) => _transform.WorldToScreen(world);

    public Point2D ScreenToWorld(Point2D screen) => _transform.ScreenToWorld(screen);

    public double WorldToScreenDistance(double mm) => _transform.WorldToScreenDistance(mm);

    public double ScreenToWorldDistance(double pixels) => _transform.ScreenToWorldDistance(pixels);

    // ── Navigation ──────────────────────────────────────────────────

    /// <summary>Multiplies the zoom by <paramref name="factor"/>, keeping the world point under <paramref name="screenPoint"/> fixed.</summary>
    public void ZoomAt(Point2D screenPoint, double factor)
    {
        _transform.ZoomBy(screenPoint, factor);
        RaiseViewChanged();
    }

    /// <summary>
    /// Mouse-wheel zoom around the cursor. <paramref name="wheelDelta"/> is WPF's delta (±120 per notch);
    /// fractional deltas from precision touchpads zoom proportionally.
    /// </summary>
    public void ZoomAtWheel(Point2D screenPoint, int wheelDelta)
    {
        if (wheelDelta == 0) return;
        ZoomAt(screenPoint, Math.Pow(Settings.ZoomFactor, wheelDelta / WheelDeltaPerNotch));
    }

    public void ZoomIn() => ZoomAt(ViewportCenter, Settings.ZoomFactor);

    public void ZoomOut() => ZoomAt(ViewportCenter, 1.0 / Settings.ZoomFactor);

    /// <summary>Moves the view by a screen-pixel delta (content follows the mouse). Domain geometry is untouched.</summary>
    public void Pan(double deltaX, double deltaY)
    {
        _transform.PanByScreenDelta(new Vector2D(deltaX, deltaY));
        RaiseViewChanged();
    }

    /// <summary>Zooms and pans so <paramref name="bounds"/> fills the viewport, leaving the configured margin.</summary>
    public void FitToBounds(BoundingBox2D bounds)
    {
        _transform.FitTo(bounds, ViewportWidth, ViewportHeight, Settings.FitMarginFraction);
        RaiseViewChanged();
    }

    /// <summary>Fits all content layers, or resets the view if there is no content.</summary>
    public void FitToContent()
    {
        var bounds = ContentBounds;
        if (bounds.IsEmpty)
            ResetView();
        else
            FitToBounds(bounds);
    }

    /// <summary>Default view: 100 % zoom with the world origin a fixed margin in from the top-left corner.</summary>
    public void ResetView()
    {
        _transform.Reset();
        double margin = Settings.ResetOriginMarginPixels;
        _transform.PanByScreenDelta(new Vector2D(margin, margin));
        RaiseViewChanged();
    }

    private Point2D ViewportCenter => new(ViewportWidth / 2.0, ViewportHeight / 2.0);

    private void RaiseViewChanged()
    {
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(PanX));
        OnPropertyChanged(nameof(PanY));
        OnPropertyChanged(nameof(ZoomPercentage));
        ViewChanged?.Invoke();
    }
}
