using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Core.Design;

/// <summary>Which side of the frame a dimension is drawn on.</summary>
public enum DimensionSide
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>
/// A dimension derived from the frame geometry (frame-relative mm). Never stored and never typed in:
/// it is recomputed from the model whenever the design changes.
/// </summary>
public readonly record struct AutoDimension(Point2D Start, Point2D End, DimensionOrientation Orientation, DimensionSide Side)
{
    /// <summary>The measured distance in mm along the dimension's axis.</summary>
    public double Value => Orientation == DimensionOrientation.Horizontal
        ? Math.Abs(End.X - Start.X)
        : Math.Abs(End.Y - Start.Y);
}

/// <summary>
/// Automatic frame dimensions: overall width (top) and height (left), plus chain dimensions to the
/// mullion centrelines (bottom) and transom centrelines (right) when divisions exist.
/// </summary>
public static class AutoDimensions
{
    public static IReadOnlyList<AutoDimension> Compute(Frame frame)
    {
        var result = new List<AutoDimension>
        {
            new(new Point2D(0, 0), new Point2D(frame.Width, 0), DimensionOrientation.Horizontal, DimensionSide.Top),
            new(new Point2D(0, 0), new Point2D(0, frame.Height), DimensionOrientation.Vertical, DimensionSide.Left)
        };

        var mullions = DistinctPositions(frame, ProfileType.Mullion);
        if (mullions.Count > 0)
        {
            var stops = mullions.Prepend(0).Append(frame.Width).ToList();
            for (int i = 0; i + 1 < stops.Count; i++)
                result.Add(new AutoDimension(new Point2D(stops[i], frame.Height), new Point2D(stops[i + 1], frame.Height),
                    DimensionOrientation.Horizontal, DimensionSide.Bottom));
        }

        var transoms = DistinctPositions(frame, ProfileType.Transom);
        if (transoms.Count > 0)
        {
            var stops = transoms.Prepend(0).Append(frame.Height).ToList();
            for (int i = 0; i + 1 < stops.Count; i++)
                result.Add(new AutoDimension(new Point2D(frame.Width, stops[i]), new Point2D(frame.Width, stops[i + 1]),
                    DimensionOrientation.Vertical, DimensionSide.Right));
        }

        return result;
    }

    private static List<double> DistinctPositions(Frame frame, ProfileType type)
    {
        var result = new List<double>();
        foreach (double p in frame.Profiles.Where(p => p.ProfileType == type).Select(Members.DivisionPosition).OrderBy(p => p))
            if (result.Count == 0 || p - result[^1] > GeometryTolerance.Default)
                result.Add(p);
        return result;
    }
}
