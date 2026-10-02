namespace Mark.Core.Geometry;

/// <summary>
/// Maps world coordinates (mm) to screen coordinates (device-independent pixels) and back.
///
///   screen = (world - Pan) * Zoom
///   world  = screen / Zoom + Pan
///
/// Pan is the world point (mm) that appears at the screen origin (top-left of the canvas).
/// Zoom is pixels per millimetre. Both spaces use the same orientation (X → right, Y → down),
/// so no axis flip is needed. Lives in Core so it can be unit-tested without WPF; the renderer
/// converts <see cref="WorldToScreenTransform"/> into its own matrix type.
/// </summary>
public class ViewportTransform
{
    /// <summary>Default lower zoom limit; override per instance with <see cref="SetZoomLimits"/>.</summary>
    public const double MinZoom = 0.01;

    /// <summary>Default upper zoom limit; override per instance with <see cref="SetZoomLimits"/>.</summary>
    public const double MaxZoom = 50.0;

    public const double DefaultZoom = 1.0;
    public const double DefaultFitMarginFraction = 0.1;

    /// <summary>Smallest content extent (mm) used by <see cref="FitTo"/>, so a point or line never yields infinite zoom.</summary>
    public const double MinFitExtentMm = 1.0;

    private double _zoom = DefaultZoom;
    private double _panX;
    private double _panY;
    private double _minimumZoom = MinZoom;
    private double _maximumZoom = MaxZoom;

    /// <summary>Current lower zoom limit (pixels per mm).</summary>
    public double MinimumZoom => _minimumZoom;

    /// <summary>Current upper zoom limit (pixels per mm).</summary>
    public double MaximumZoom => _maximumZoom;

    /// <summary>
    /// Pixels per millimetre, clamped to [<see cref="MinimumZoom"/>, <see cref="MaximumZoom"/>].
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    public double Zoom
    {
        get => _zoom;
        set
        {
            GeometryValidation.EnsureFinite(value);
            _zoom = Math.Clamp(value, _minimumZoom, _maximumZoom);
        }
    }

    /// <summary>Sets the allowed zoom range and re-clamps the current zoom into it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A limit is not positive/finite, or minimum exceeds maximum.</exception>
    public void SetZoomLimits(double minimum, double maximum)
    {
        GeometryValidation.EnsurePositive(minimum);
        GeometryValidation.EnsurePositive(maximum);
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "Minimum zoom must not exceed maximum zoom.");

        _minimumZoom = minimum;
        _maximumZoom = maximum;
        Zoom = _zoom;
    }

    /// <summary>World X (mm) shown at the left edge of the screen.</summary>
    public double PanX
    {
        get => _panX;
        set
        {
            GeometryValidation.EnsureFinite(value);
            _panX = value;
        }
    }

    /// <summary>World Y (mm) shown at the top edge of the screen.</summary>
    public double PanY
    {
        get => _panY;
        set
        {
            GeometryValidation.EnsureFinite(value);
            _panY = value;
        }
    }

    // ── Conversion ──────────────────────────────────────────────────

    public Point2D WorldToScreen(Point2D world)
        => new((world.X - PanX) * Zoom, (world.Y - PanY) * Zoom);

    public Point2D ScreenToWorld(Point2D screen)
        => new(screen.X / Zoom + PanX, screen.Y / Zoom + PanY);

    public double WorldToScreenDistance(double mm) => mm * Zoom;

    public double ScreenToWorldDistance(double pixels) => pixels / Zoom;

    /// <summary>The world → screen mapping as an affine transform (for renderers).</summary>
    public Transform2D WorldToScreenTransform
        => Transform2D.Translation(-PanX, -PanY).Then(Transform2D.Scale(Zoom));

    /// <summary>The screen → world mapping as an affine transform.</summary>
    public Transform2D ScreenToWorldTransform => WorldToScreenTransform.Inverse();

    /// <summary>The world-space rectangle currently visible in a viewport of the given pixel size.</summary>
    public Rectangle2D VisibleWorldBounds(double viewportWidth, double viewportHeight)
    {
        GeometryValidation.EnsureNonNegative(viewportWidth);
        GeometryValidation.EnsureNonNegative(viewportHeight);
        return new Rectangle2D(PanX, PanY, viewportWidth / Zoom, viewportHeight / Zoom);
    }

    // ── Zoom & pan ──────────────────────────────────────────────────

    /// <summary>
    /// Sets the zoom to <paramref name="newZoom"/> while keeping the world point under
    /// <paramref name="screenAnchor"/> fixed on screen (zoom-around-cursor).
    /// </summary>
    public void ZoomAt(Point2D screenAnchor, double newZoom)
    {
        GeometryValidation.EnsureFinite(screenAnchor);
        Point2D worldAnchor = ScreenToWorld(screenAnchor);
        Zoom = newZoom;
        // Solve screenAnchor = (worldAnchor − Pan) · Zoom for Pan.
        PanX = worldAnchor.X - screenAnchor.X / Zoom;
        PanY = worldAnchor.Y - screenAnchor.Y / Zoom;
    }

    /// <summary>Multiplies the zoom by <paramref name="factor"/> around <paramref name="screenAnchor"/>.</summary>
    public void ZoomBy(Point2D screenAnchor, double factor)
    {
        GeometryValidation.EnsurePositive(factor);
        ZoomAt(screenAnchor, Zoom * factor);
    }

    /// <summary>Shifts the view by a screen-space delta (e.g. a mouse drag). World geometry is unchanged.</summary>
    public void PanByScreenDelta(Vector2D screenDelta)
    {
        GeometryValidation.EnsureFinite(screenDelta);
        PanX -= screenDelta.X / Zoom;
        PanY -= screenDelta.Y / Zoom;
    }

    public void Reset()
    {
        Zoom = DefaultZoom;
        PanX = 0;
        PanY = 0;
    }

    /// <summary>
    /// Sets zoom and pan so <paramref name="content"/> is centred in the viewport,
    /// leaving <paramref name="marginFraction"/> of the viewport as empty border.
    /// An empty box or zero-size viewport resets the view.
    /// </summary>
    public void FitTo(BoundingBox2D content, double viewportWidth, double viewportHeight,
        double marginFraction = DefaultFitMarginFraction)
    {
        if (!double.IsFinite(marginFraction) || marginFraction < 0 || marginFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(marginFraction), marginFraction, "Margin fraction must be in [0, 1).");

        if (content.IsEmpty || !(viewportWidth > 0) || !(viewportHeight > 0))
        {
            Reset();
            return;
        }

        // A single point or line has no area; give it a minimal extent so zoom stays finite.
        double contentW = Math.Max(content.Width, MinFitExtentMm);
        double contentH = Math.Max(content.Height, MinFitExtentMm);

        double zoomX = viewportWidth / contentW * (1.0 - marginFraction);
        double zoomY = viewportHeight / contentH * (1.0 - marginFraction);
        Zoom = Math.Min(zoomX, zoomY);

        Point2D center = content.Center;
        PanX = center.X - viewportWidth / Zoom / 2.0;
        PanY = center.Y - viewportHeight / Zoom / 2.0;
    }
}
