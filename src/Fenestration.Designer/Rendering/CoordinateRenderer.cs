using System.Globalization;
using System.Windows;
using Fenestration.Core.Geometry;
using Fenestration.Core.Viewport;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Draws the coordinate indicators: the world axes through the origin and ruler labels (in mm)
/// on the major grid lines along the top and left edges of the viewport.
/// </summary>
public sealed class CoordinateRenderer
{
    /// <summary>Ruler labels closer together than this (pixels) are thinned out.</summary>
    private const double MinLabelPixelSpacing = 70.0;

    private const double LabelInset = 3.0;

    /// <summary>Height (px) of the top row used by horizontal ruler labels.</summary>
    private const double RulerRowPixels = 16.0;

    /// <summary>Width (px) at the left edge kept free of horizontal ruler labels, for the vertical ruler.</summary>
    private const double CornerReservePixels = 36.0;

    /// <summary>Distance (px) of the origin "0" label from the origin, up and to the left.</summary>
    private const double OriginLabelOffset = 10.0;

    public void RenderAxes(ViewportDrawingContext context)
    {
        Point origin = context.ToScreen(Point2D.Origin);
        double width = context.ViewportSize.Width;
        double height = context.ViewportSize.Height;
        var dc = context.DrawingContext;

        bool xAxisVisible = origin.Y >= 0 && origin.Y <= height;
        bool yAxisVisible = origin.X >= 0 && origin.X <= width;

        if (xAxisVisible)
        {
            double y = ViewportDrawingContext.Crisp(origin.Y);
            dc.DrawLine(ViewportTheme.XAxisPen, new Point(0, y), new Point(width, y));
        }

        if (yAxisVisible)
        {
            double x = ViewportDrawingContext.Crisp(origin.X);
            dc.DrawLine(ViewportTheme.YAxisPen, new Point(x, 0), new Point(x, height));
        }

        // Above-left of the origin: content usually starts at (0,0) and extends right/down, so this corner stays clear.
        if (xAxisVisible && yAxisVisible)
            context.DrawText("0", new Point(origin.X - OriginLabelOffset, origin.Y - OriginLabelOffset - LabelInset),
                ViewportTheme.OriginLabelBrush);
    }

    public void RenderRulerLabels(ViewportDrawingContext context, GridSpacing spacing, int maxLinesPerAxis)
    {
        var view = context.VisibleWorldBounds;
        var transform = context.Transform;

        // Label every major line, or every n-th one if they would crowd each other.
        double majorPixels = spacing.MajorMm * transform.Zoom;
        long labelEvery = Math.Max(1, (long)Math.Ceiling(MinLabelPixelSpacing / majorPixels));
        double labelSpacing = spacing.MajorMm * labelEvery;

        var xs = GridAxisRange.Visible(view.Left, view.Right, labelSpacing);
        var ys = GridAxisRange.Visible(view.Top, view.Bottom, labelSpacing);
        if (xs.Count > maxLinesPerAxis || ys.Count > maxLinesPerAxis)
            return;

        for (long i = xs.FirstIndex; i <= xs.LastIndex; i++)
        {
            double worldX = xs.PositionAt(i);
            if (worldX == 0) continue; // the origin label covers it
            double screenX = (worldX - transform.PanX) * transform.Zoom;
            if (screenX < CornerReservePixels) continue; // the top-left corner belongs to the vertical ruler
            context.DrawText(Format(worldX), new Point(screenX + LabelInset, LabelInset), ViewportTheme.RulerLabelBrush);
        }

        for (long j = ys.FirstIndex; j <= ys.LastIndex; j++)
        {
            double worldY = ys.PositionAt(j);
            if (worldY == 0) continue;
            double screenY = (worldY - transform.PanY) * transform.Zoom;
            if (screenY < RulerRowPixels) continue; // would overlap the horizontal ruler's labels
            context.DrawText(Format(worldY), new Point(LabelInset, screenY + LabelInset), ViewportTheme.RulerLabelBrush);
        }
    }

    private static string Format(double mm) => mm.ToString("0.###", CultureInfo.InvariantCulture);
}
