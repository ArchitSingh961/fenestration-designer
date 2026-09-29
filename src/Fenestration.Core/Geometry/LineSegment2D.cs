using System.Globalization;

namespace Fenestration.Core.Geometry;

/// <summary>
/// Immutable line segment between two points in world coordinates (mm).
/// Parameter t runs along the segment: t = 0 → Start, t = 1 → End.
/// </summary>
public readonly struct LineSegment2D : IEquatable<LineSegment2D>
{
    public Point2D Start { get; }
    public Point2D End { get; }

    public LineSegment2D(Point2D start, Point2D end)
    {
        Start = start;
        End = end;
    }

    public LineSegment2D(double x1, double y1, double x2, double y2)
        : this(new Point2D(x1, y1), new Point2D(x2, y2)) { }

    // ── Properties ──────────────────────────────────────────────────

    public double Length => Start.DistanceTo(End);

    public double LengthSquared => Start.DistanceSquaredTo(End);

    public Point2D Midpoint => Start.MidpointTo(End);

    /// <summary>Unit-length direction from Start to End (<see cref="Vector2D.Zero"/> if degenerate).</summary>
    public Vector2D Direction => Delta.Normalized();

    /// <summary>The un-normalized vector from Start to End.</summary>
    public Vector2D Delta => End - Start;

    /// <summary>Axis-aligned bounds of the segment.</summary>
    public BoundingBox2D Bounds => BoundingBox2D.FromSegment(this);

    /// <summary>True if the segment is no longer than <paramref name="tolerance"/> (effectively a point).</summary>
    public bool IsDegenerate(double tolerance = GeometryTolerance.Default)
        => LengthSquared <= tolerance * tolerance;

    // ── Parametric access ───────────────────────────────────────────

    /// <summary>Point at parameter <paramref name="t"/>: 0 → Start, 0.5 → Midpoint, 1 → End.</summary>
    public Point2D PointAt(double t) => Start.Lerp(End, t);

    /// <summary>Closest point on the segment (clamped to [Start, End]).</summary>
    public Point2D ClosestPoint(Point2D point) => Projection2D.ProjectPointOntoSegment(point, this).Point;

    /// <summary>Shortest distance from <paramref name="point"/> to the segment.</summary>
    public double DistanceTo(Point2D point) => Projection2D.ProjectPointOntoSegment(point, this).Distance;

    /// <summary>True if <paramref name="point"/> lies on the segment within <paramref name="tolerance"/> mm.</summary>
    public bool ContainsPoint(Point2D point, double tolerance = GeometryTolerance.Default)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);
        return DistanceTo(point) <= tolerance;
    }

    /// <summary>The same segment with Start and End swapped.</summary>
    public LineSegment2D Reversed() => new(End, Start);

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(LineSegment2D other) => Start == other.Start && End == other.End;
    public override bool Equals(object? obj) => obj is LineSegment2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Start, End);

    public static bool operator ==(LineSegment2D left, LineSegment2D right) => left.Equals(right);
    public static bool operator !=(LineSegment2D left, LineSegment2D right) => !left.Equals(right);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{Start} → {End}]");
}
