using System.Globalization;

namespace Fenestration.Core.Geometry;

/// <summary>
/// Immutable 2D affine transform (translation, scale, rotation and combinations), independent of WPF.
///
///   x' = M11·x + M12·y + OffsetX
///   y' = M21·x + M22·y + OffsetY
///
/// Compose with <see cref="Then"/>: <c>a.Then(b)</c> applies <c>a</c> first, then <c>b</c>.
/// Rotation angles are in degrees, positive from +X towards +Y — clockwise on screen, since Y points down.
/// </summary>
public readonly struct Transform2D : IEquatable<Transform2D>
{
    public double M11 { get; }
    public double M12 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    /// <exception cref="ArgumentOutOfRangeException">Any component is not finite.</exception>
    public Transform2D(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        GeometryValidation.EnsureFinite(m11);
        GeometryValidation.EnsureFinite(m12);
        GeometryValidation.EnsureFinite(m21);
        GeometryValidation.EnsureFinite(m22);
        GeometryValidation.EnsureFinite(offsetX);
        GeometryValidation.EnsureFinite(offsetY);
        M11 = m11; M12 = m12; M21 = m21; M22 = m22;
        OffsetX = offsetX; OffsetY = offsetY;
    }

    // ── Factories ───────────────────────────────────────────────────

    public static Transform2D Identity => new(1, 0, 0, 1, 0, 0);

    public static Transform2D Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    public static Transform2D Translation(Vector2D offset) => Translation(offset.X, offset.Y);

    public static Transform2D Scale(double factor) => Scale(factor, factor);

    public static Transform2D Scale(double scaleX, double scaleY) => new(scaleX, 0, 0, scaleY, 0, 0);

    /// <summary>Scales about <paramref name="center"/>, which stays fixed.</summary>
    public static Transform2D Scale(double scaleX, double scaleY, Point2D center)
        => Translation(-center.X, -center.Y).Then(Scale(scaleX, scaleY)).Then(Translation(center.X, center.Y));

    /// <summary>Rotates about the origin by <paramref name="degrees"/> (positive = towards +Y).</summary>
    public static Transform2D Rotation(double degrees)
    {
        (double sin, double cos) = SinCosDegrees(degrees);
        return new Transform2D(cos, -sin, sin, cos, 0, 0);
    }

    /// <summary>Rotates about <paramref name="center"/>, which stays fixed.</summary>
    public static Transform2D Rotation(double degrees, Point2D center)
        => Translation(-center.X, -center.Y).Then(Rotation(degrees)).Then(Translation(center.X, center.Y));

    // ── Composition & inversion ─────────────────────────────────────

    /// <summary>Returns a transform that applies this one first, then <paramref name="next"/>.</summary>
    public Transform2D Then(Transform2D next) => new(
        next.M11 * M11 + next.M12 * M21,
        next.M11 * M12 + next.M12 * M22,
        next.M21 * M11 + next.M22 * M21,
        next.M21 * M12 + next.M22 * M22,
        next.M11 * OffsetX + next.M12 * OffsetY + next.OffsetX,
        next.M21 * OffsetX + next.M22 * OffsetY + next.OffsetY);

    public double Determinant => M11 * M22 - M12 * M21;

    /// <summary>False for singular transforms (e.g. a zero scale), which collapse space and cannot be undone.</summary>
    public bool IsInvertible => Math.Abs(Determinant) > GeometryTolerance.Epsilon;

    /// <exception cref="InvalidOperationException">The transform is singular.</exception>
    public Transform2D Inverse()
        => TryInvert(out var inverse)
            ? inverse
            : throw new InvalidOperationException("Transform is not invertible (determinant is zero).");

    public bool TryInvert(out Transform2D inverse)
    {
        if (!IsInvertible)
        {
            inverse = Identity;
            return false;
        }

        double det = Determinant;
        // Inverse of the 2×2 linear part, then undo the offset in the inverted frame.
        double i11 = M22 / det, i12 = -M12 / det;
        double i21 = -M21 / det, i22 = M11 / det;
        inverse = new Transform2D(
            i11, i12, i21, i22,
            -(i11 * OffsetX + i12 * OffsetY),
            -(i21 * OffsetX + i22 * OffsetY));
        return true;
    }

    // ── Application ─────────────────────────────────────────────────

    public Point2D TransformPoint(Point2D p)
        => new(M11 * p.X + M12 * p.Y + OffsetX, M21 * p.X + M22 * p.Y + OffsetY);

    /// <summary>Transforms a direction/offset: rotation and scale apply, translation does not.</summary>
    public Vector2D TransformVector(Vector2D v)
        => new(M11 * v.X + M12 * v.Y, M21 * v.X + M22 * v.Y);

    public LineSegment2D TransformSegment(LineSegment2D segment)
        => new(TransformPoint(segment.Start), TransformPoint(segment.End));

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(Transform2D other)
        => M11.Equals(other.M11) && M12.Equals(other.M12) && M21.Equals(other.M21) && M22.Equals(other.M22)
        && OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY);

    public bool AlmostEquals(Transform2D other, double tolerance = GeometryTolerance.Epsilon)
        => Math.Abs(M11 - other.M11) <= tolerance && Math.Abs(M12 - other.M12) <= tolerance
        && Math.Abs(M21 - other.M21) <= tolerance && Math.Abs(M22 - other.M22) <= tolerance
        && Math.Abs(OffsetX - other.OffsetX) <= tolerance && Math.Abs(OffsetY - other.OffsetY) <= tolerance;

    public override bool Equals(object? obj) => obj is Transform2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);

    public static bool operator ==(Transform2D left, Transform2D right) => left.Equals(right);
    public static bool operator !=(Transform2D left, Transform2D right) => !left.Equals(right);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"[{M11:0.####} {M12:0.####} {OffsetX:0.###}; {M21:0.####} {M22:0.####} {OffsetY:0.###}]");

    /// <summary>
    /// Sine and cosine of an angle in degrees, exact for multiples of 90°.
    /// Fenestration geometry is overwhelmingly orthogonal; Math.Cos(π/2) ≈ 6e-17 would otherwise
    /// leave rotated mullions a hair off vertical.
    /// </summary>
    private static (double Sin, double Cos) SinCosDegrees(double degrees)
    {
        double normalized = AngleMath.NormalizeAngle(degrees);
        return normalized switch
        {
            0.0 => (0.0, 1.0),
            90.0 => (1.0, 0.0),
            180.0 => (0.0, -1.0),
            270.0 => (-1.0, 0.0),
            _ => Math.SinCos(AngleMath.DegreesToRadians(normalized))
        };
    }
}
