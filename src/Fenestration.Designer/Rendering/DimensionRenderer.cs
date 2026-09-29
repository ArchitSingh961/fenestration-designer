using System.Globalization;
using System.Windows;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Draws <see cref="AutoDimension"/>s CAD-style: extension lines from the frame, a dimension line a fixed
/// pixel distance outside the frame, 45° ticks, and the value in mm (read from the geometry, never typed).
/// Offsets are in pixels, so dimensions stay readable at every zoom.
/// </summary>
public sealed class DimensionRenderer
{
    /// <summary>Segments shorter than this (px) are drawn without text.</summary>
    private const double MinPixelsForText = 18.0;

    public void Render(ViewportDrawingContext context, Frame frame)
    {
        var origin = new Vector2D(frame.X, frame.Y);
        var outerTopLeft = context.ToScreen(new Point2D(frame.X, frame.Y));
        var outerBottomRight = context.ToScreen(new Point2D(frame.X + frame.Width, frame.Y + frame.Height));

        foreach (var dim in AutoDimensions.Compute(frame))
        {
            var a = context.ToScreen(dim.Start + origin);
            var b = context.ToScreen(dim.End + origin);
            string label = dim.Value.ToString("0.#", CultureInfo.InvariantCulture);

            switch (dim.Side)
            {
                case DimensionSide.Top:
                    DrawHorizontal(context, a.X, b.X, outerTopLeft.Y, -1, label);
                    break;
                case DimensionSide.Bottom:
                    DrawHorizontal(context, a.X, b.X, outerBottomRight.Y, +1, label);
                    break;
                case DimensionSide.Left:
                    DrawVertical(context, a.Y, b.Y, outerTopLeft.X, -1, label);
                    break;
                case DimensionSide.Right:
                    DrawVertical(context, a.Y, b.Y, outerBottomRight.X, +1, label);
                    break;
            }
        }
    }

    /// <param name="edgeY">Screen Y of the frame edge being dimensioned.</param>
    /// <param name="direction">-1 = above the frame, +1 = below.</param>
    private static void DrawHorizontal(ViewportDrawingContext context, double x1, double x2, double edgeY, int direction, string label)
    {
        var dc = context.DrawingContext;
        var pen = DesignTheme.DimensionPen;
        double lineY = edgeY + direction * DesignTheme.DimensionOffsetPixels;

        foreach (double x in new[] { x1, x2 })
        {
            dc.DrawLine(pen, new Point(x, edgeY + direction * DesignTheme.ExtensionGapPixels),
                new Point(x, lineY + direction * DesignTheme.ExtensionOvershootPixels));
            dc.DrawLine(pen, new Point(x - DesignTheme.TickHalfPixels, lineY + DesignTheme.TickHalfPixels),
                new Point(x + DesignTheme.TickHalfPixels, lineY - DesignTheme.TickHalfPixels));
        }
        dc.DrawLine(pen, new Point(x1, lineY), new Point(x2, lineY));

        if (Math.Abs(x2 - x1) < MinPixelsForText) return;
        var text = context.CreateText(label, DesignTheme.DimensionText, DesignTheme.DimensionFontSize);
        context.DrawTextCentered(text, new Point((x1 + x2) / 2, lineY - text.Height / 2 - 1));
    }

    /// <param name="edgeX">Screen X of the frame edge being dimensioned.</param>
    /// <param name="direction">-1 = left of the frame, +1 = right.</param>
    private static void DrawVertical(ViewportDrawingContext context, double y1, double y2, double edgeX, int direction, string label)
    {
        var dc = context.DrawingContext;
        var pen = DesignTheme.DimensionPen;
        double lineX = edgeX + direction * DesignTheme.DimensionOffsetPixels;

        foreach (double y in new[] { y1, y2 })
        {
            dc.DrawLine(pen, new Point(edgeX + direction * DesignTheme.ExtensionGapPixels, y),
                new Point(lineX + direction * DesignTheme.ExtensionOvershootPixels, y));
            dc.DrawLine(pen, new Point(lineX - DesignTheme.TickHalfPixels, y + DesignTheme.TickHalfPixels),
                new Point(lineX + DesignTheme.TickHalfPixels, y - DesignTheme.TickHalfPixels));
        }
        dc.DrawLine(pen, new Point(lineX, y1), new Point(lineX, y2));

        if (Math.Abs(y2 - y1) < MinPixelsForText) return;
        // Vertical text reads bottom-to-top and sits on the left of its dimension line.
        var text = context.CreateText(label, DesignTheme.DimensionText, DesignTheme.DimensionFontSize);
        context.DrawTextCentered(text, new Point(lineX - text.Height / 2 - 1, (y1 + y2) / 2), rotationDegrees: -90);
    }
}
