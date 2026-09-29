using System.Globalization;

namespace Fenestration.Core.Geometry;

/// <summary>
/// Immutable 2D vector. Used for directions, offsets, and geometric computations.
/// All values are in millimetres. Equality is exact; use <see cref="AlmostEquals"/> for tolerance.
/// </summary>
public readonly struct Vector2D : IEquatable<Vector2D>
{
    public double X { get; }
    public double Y { get; }

    public Vector2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    // ── Predefined vectors ──────────────────────────────────────────

    public static Vector2D Zero => new(0, 0);
    public static Vector2D UnitX => new(1, 0);
    public static Vector2D UnitY => new(0, 1);

    // ── Properties ──────────────────────────────────────────────────

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    /// <summary>True if neither component is NaN or ±Infinity.</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>
    /// Returns a unit-length vector in the same direction.
    /// A (near-)zero vector has no direction, so <see cref="Zero"/> is returned instead of NaN.
    /// </summary>
    public Vector2D Normalized()
    {
        double len = Length;
        if (len <= GeometryTolerance.Epsilon)
            return Zero;
        return new Vector2D(X / len, Y / len);
    }

    /// <summary>
    /// Returns this vector rotated +90° (from +X towards +Y). Because world Y points down,
    /// this appears as a clockwise quarter-turn on screen.
    /// </summary>
    public Vector2D Perpendicular() => new(-Y, X);

    // ── Operations ──────────────────────────────────────────────────

    public double Dot(Vector2D other) => X * other.X + Y * other.Y;

    /// <summary>
    /// 2D cross product (z-component of the 3D cross product).
    /// Positive when <paramref name="other"/> lies clockwise-on-screen (towards +Y) from this vector;
    /// zero when the vectors are parallel.
    /// </summary>
    public double Cross(Vector2D other) => X * other.Y - Y * other.X;

    /// <summary>True if the two vectors differ by at most <paramref name="tolerance"/> in length.</summary>
    public bool AlmostEquals(Vector2D other, double tolerance = GeometryTolerance.Default)
        => (this - other).LengthSquared <= tolerance * tolerance;

    // ── Operator overloads ──────────────────────────────────────────

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator *(Vector2D v, double s) => new(v.X * s, v.Y * s);
    public static Vector2D operator *(double s, Vector2D v) => new(v.X * s, v.Y * s);
    public static Vector2D operator -(Vector2D v) => new(-v.X, -v.Y);

    /// <exception cref="DivideByZeroException">The divisor is zero or not finite.</exception>
    public static Vector2D operator /(Vector2D v, double s)
    {
        if (s == 0 || !double.IsFinite(s))
            throw new DivideByZeroException($"Cannot divide a vector by {s}.");
        return new(v.X / s, v.Y / s);
    }

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(Vector2D other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Vector2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Vector2D left, Vector2D right) => left.Equals(right);
    public static bool operator !=(Vector2D left, Vector2D right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"<{X:0.###}, {Y:0.###}>");
}
