using System.Windows;
using System.Windows.Media;
using Mark.Core.Viewport;

namespace Mark.Designer.Rendering;

/// <summary>
/// Draws the adaptive world-space grid. Only lines inside the visible world rectangle are generated,
/// and all minor lines go into one frozen <see cref="StreamGeometry"/> (likewise major lines),
/// so a full grid costs two draw calls with no per-line WPF objects.
/// </summary>
public sealed class GridRenderer
{
    public void Render(ViewportDrawingContext context, GridSpacing spacing, int maxLinesPerAxis)
    {
        var view = context.VisibleWorldBounds;

        var minorX = GridAxisRange.Visible(view.Left, view.Right, spacing.MinorMm);
        var minorY = GridAxisRange.Visible(view.Top, view.Bottom, spacing.MinorMm);
        var majorX = GridAxisRange.Visible(view.Left, view.Right, spacing.MajorMm);
        var majorY = GridAxisRange.Visible(view.Top, view.Bottom, spacing.MajorMm);

        bool drawMinor = minorX.Count <= maxLinesPerAxis && minorY.Count <= maxLinesPerAxis;
        bool drawMajor = majorX.Count <= maxLinesPerAxis && majorY.Count <= maxLinesPerAxis;

        if (drawMinor)
            DrawLines(context, ViewportTheme.MinorGridPen, minorX, minorY, skipEvery: spacing.MajorEvery);
        if (drawMajor)
            DrawLines(context, ViewportTheme.MajorGridPen, majorX, majorY, skipEvery: 0);
    }

    /// <param name="skipEvery">Skip indices divisible by this (major positions, drawn separately); 0 = skip none.</param>
    private static void DrawLines(ViewportDrawingContext context, Pen pen, GridAxisRange xs, GridAxisRange ys, int skipEvery)
    {
        double width = context.ViewportSize.Width;
        double height = context.ViewportSize.Height;
        var transform = context.Transform;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            for (long i = xs.FirstIndex; i <= xs.LastIndex; i++)
            {
                if (skipEvery > 0 && i % skipEvery == 0) continue;
                double x = ViewportDrawingContext.Crisp((xs.PositionAt(i) - transform.PanX) * transform.Zoom);
                g.BeginFigure(new Point(x, 0), isFilled: false, isClosed: false);
                g.LineTo(new Point(x, height), isStroked: true, isSmoothJoin: false);
            }

            for (long j = ys.FirstIndex; j <= ys.LastIndex; j++)
            {
                if (skipEvery > 0 && j % skipEvery == 0) continue;
                double y = ViewportDrawingContext.Crisp((ys.PositionAt(j) - transform.PanY) * transform.Zoom);
                g.BeginFigure(new Point(0, y), isFilled: false, isClosed: false);
                g.LineTo(new Point(width, y), isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        context.DrawingContext.DrawGeometry(null, pen, geometry);
    }
}
