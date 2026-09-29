using System.Globalization;

namespace Fenestration.Core.Geometry;

/// <summary>
/// Immutable 2D point in world coordinates (millimetres).
/// Coordinate system: origin top-left, X → right, Y → down.
///
/// Equality (==, Equals, GetHashCode) is EXACT so the type is safe as a dictionary/set key.
/// Use <see cref="AlmostEquals"/> for tolerance-based geometric comparison.
/// </summary>
public readonly struct Point2D : IEquatable<Point2D>
{
    public double X { get; }
    public double Y { get; }

    public Point2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The origin (0, 0).</summary>
    public static Point2D Origin => new(0, 0);

    /// <summary>True if neither coordinate is NaN or ±Infinity.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public double DistanceTo(Point2D other) => Math.Sqrt(DistanceSquaredTo(other));

    /// <summary>Squared distance — cheaper than <see cref="DistanceTo"/> when only comparing distances.</summary>
    public double DistanceSquaredTo(Point2D other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Linear interpolation: t = 0 → this, t = 1 → <paramref name="other"/>.
    /// Values outside [0, 1] extrapolate along the same line.
    /// </summary>
    public Point2D Lerp(Point2D other, double t)
        => new(X + (other.X - X) * t, Y + (other.Y - Y) * t);

    /// <summary>Returns the midpoint between this point and <paramref name="other"/>.</summary>
    public Point2D MidpointTo(Point2D other)
        => new((X + other.X) / 2.0, (Y + other.Y) / 2.0);

    /// <summary>Translates this point by a vector.</summary>
    public Point2D Translate(Vector2D v) => new(X + v.X, Y + v.Y);

    /// <summary>The vector from the origin to this point.</summary>
    public Vector2D ToVector() => new(X, Y);

    /// <summary>True if the two points are within <paramref name="tolerance"/> mm of each other.</summary>
    public bool AlmostEquals(Point2D other, double tolerance = GeometryTolerance.Default)
        => DistanceSquaredTo(other) <= tolerance * tolerance;

    // ── Operator overloads ──────────────────────────────────────────

    public static Vector2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator +(Point2D p, Vector2D v) => new(p.X + v.X, p.Y + v.Y);
    public static Point2D operator -(Point2D p, Vector2D v) => new(p.X - v.X, p.Y - v.Y);

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Point2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Point2D left, Point2D right) => left.Equals(right);
    public static bool operator !=(Point2D left, Point2D right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X:0.###}, {Y:0.###})");
}
