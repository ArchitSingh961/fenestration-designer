using Mark.Core.Geometry;
using Mark.Core.Interfaces;

namespace Mark.Core.Snapping;

/// <summary>World axis for one-dimensional snapping (e.g. a mullion only moves in X).</summary>
public enum SnapAxis
{
    X,
    Y
}

/// <summary>
/// Something that can be snapped to, in world millimetres: either a point (endpoint, midpoint,
/// intersection, centre) or a line segment (edge), onto which the cursor is projected.
/// </summary>
public readonly record struct SnapTarget(SnapType Type, Point2D Point, LineSegment2D? Line)
{
    public static SnapTarget AtPoint(SnapType type, Point2D point) => new(type, point, null);

    public static SnapTarget OnLine(SnapType type, LineSegment2D line) => new(type, line.Midpoint, line);

    public bool IsLine => Line is not null;
}

/// <summary>Result of a one-dimensional snap.</summary>
/// <param name="Value">The snapped coordinate (unchanged if <paramref name="Type"/> is <see cref="SnapType.None"/>).</param>
/// <param name="Type">What was snapped to.</param>
/// <param name="Target">Where the target is, for on-screen feedback (null when nothing snapped).</param>
public readonly record struct AxisSnapResult(double Value, SnapType Type, Point2D? Target)
{
    public bool IsSnapped => Type != SnapType.None;
}
