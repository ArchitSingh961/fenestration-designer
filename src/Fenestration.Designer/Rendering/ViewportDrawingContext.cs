using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Fenestration.Core.Geometry;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// A WPF <see cref="DrawingContext"/> paired with the current <see cref="ViewportTransform"/>.
/// Layers draw WORLD geometry (mm) through it; conversion to screen pixels happens here, in one place,
/// using the Core transform. This is the only bridge between Core geometry and WPF drawing types.
/// </summary>
public sealed class ViewportDrawingContext
{
    public ViewportDrawingContext(DrawingContext drawingContext, ViewportTransform transform, Size viewportSize, double pixelsPerDip)
    {
        DrawingContext = drawingContext;
        Transform = transform;
        ViewportSize = viewportSize;
        PixelsPerDip = pixelsPerDip;
        VisibleWorldBounds = transform.VisibleWorldBounds(viewportSize.Width, viewportSize.Height);
        _visibleBox = VisibleWorldBounds.Bounds;
    }

    private readonly BoundingBox2D _visibleBox;

    /// <summary>The raw WPF context, for screen-space drawing that has no world equivalent.</summary>
    public DrawingContext DrawingContext { get; }

    public ViewportTransform Transform { get; }

    /// <summary>Viewport size in screen pixels (DIPs).</summary>
    public Size ViewportSize { get; }

    public double PixelsPerDip { get; }

    /// <summary>The world rectangle (mm) currently on screen.</summary>
    public Rectangle2D VisibleWorldBounds { get; }

    public double Zoom => Transform.Zoom;

    // ── Conversion ──────────────────────────────────────────────────

    public Point ToScreen(Point2D world)
    {
        Point2D screen = Transform.WorldToScreen(world);
        return new Point(screen.X, screen.Y);
    }

    /// <summary>True if <paramref name="worldBounds"/> overlaps the visible area. Use it to skip off-screen objects.</summary>
    public bool IsVisible(BoundingBox2D worldBounds) => worldBounds.Intersects(_visibleBox);

    /// <summary>
    /// Snaps a screen coordinate to the centre of a device pixel so 1 px axis-aligned lines render crisp
    /// instead of blurred across two pixels (exact at 100 % display scaling).
    /// </summary>
    public static double Crisp(double screenCoordinate) => Math.Floor(screenCoordinate) + 0.5;

    // ── World-space drawing ─────────────────────────────────────────

    public void DrawLine(Pen pen, LineSegment2D world)
        => DrawingContext.DrawLine(pen, ToScreen(world.Start), ToScreen(world.End));

    public void DrawRectangle(Brush? fill, Pen? pen, Rectangle2D world)
        => DrawingContext.DrawRectangle(fill, pen, new Rect(ToScreen(world.TopLeft), ToScreen(world.BottomRight)));

    // ── Screen-space text ───────────────────────────────────────────

    /// <summary>Draws a label with its top-left at <paramref name="screenPosition"/>. Text never scales with zoom.</summary>
    public void DrawText(string text, Point screenPosition, Brush brush, double fontSize = ViewportTheme.LabelFontSize)
        => DrawingContext.DrawText(CreateText(text, brush, fontSize), screenPosition);

    /// <summary>Builds a label so callers can measure it (Width/Height) before placing it.</summary>
    public FormattedText CreateText(string text, Brush brush, double fontSize = ViewportTheme.LabelFontSize)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ViewportTheme.LabelTypeface, fontSize, brush, PixelsPerDip);

    /// <summary>Draws <paramref name="text"/> centred on <paramref name="screenCenter"/>, optionally rotated (degrees, clockwise).</summary>
    public void DrawTextCentered(FormattedText text, Point screenCenter, double rotationDegrees = 0)
    {
        var topLeft = new Point(screenCenter.X - text.Width / 2, screenCenter.Y - text.Height / 2);
        if (rotationDegrees == 0)
        {
            DrawingContext.DrawText(text, topLeft);
            return;
        }
        DrawingContext.PushTransform(new RotateTransform(rotationDegrees, screenCenter.X, screenCenter.Y));
        DrawingContext.DrawText(text, topLeft);
        DrawingContext.Pop();
    }
}
