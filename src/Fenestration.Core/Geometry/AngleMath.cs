namespace Fenestration.Core.Geometry;

/// <summary>
/// Angle helpers. Public values are in DEGREES (what users see); radians are used only internally.
///
/// Angles are measured from the +X axis towards the +Y axis. Because world Y points DOWN,
/// a positive angle turns CLOCKWISE on screen: 0° = right, 90° = down, 180° = left, 270° = up.
/// </summary>
public static class AngleMath
{
    public const double FullTurnDegrees = 360.0;
    public const double HalfTurnDegrees = 180.0;

    public static double DegreesToRadians(double degrees) => degrees * Math.PI / HalfTurnDegrees;

    public static double RadiansToDegrees(double radians) => radians * HalfTurnDegrees / Math.PI;

    /// <summary>Wraps an angle into [0, 360).</summary>
    public static double NormalizeAngle(double degrees)
    {
        GeometryValidation.EnsureFinite(degrees);
        double result = degrees % FullTurnDegrees;
        if (result < 0)
            result += FullTurnDegrees;
        // A tiny negative input (e.g. -1e-15) becomes exactly 360 after the addition above.
        return result >= FullTurnDegrees ? 0.0 : result;
    }

    /// <summary>Wraps an angle into (-180, 180].</summary>
    public static double NormalizeSignedAngle(double degrees)
    {
        double result = NormalizeAngle(degrees);
        return result > HalfTurnDegrees ? result - FullTurnDegrees : result;
    }

    /// <summary>
    /// Direction of <paramref name="vector"/> in [0, 360). A zero vector has no direction; 0 is returned
    /// (matching Atan2's convention) rather than NaN.
    /// </summary>
    public static double DirectionAngle(Vector2D vector)
    {
        if (vector.LengthSquared == 0)
            return 0.0;
        return NormalizeAngle(RadiansToDegrees(Math.Atan2(vector.Y, vector.X)));
    }

    /// <summary>Unsigned angle between two vectors in [0, 180]. Returns 0 if either vector is zero.</summary>
    public static double AngleBetween(Vector2D a, Vector2D b) => Math.Abs(SignedAngleBetween(a, b));

    /// <summary>
    /// Signed angle to rotate <paramref name="from"/> onto <paramref name="to"/>, in (-180, 180].
    /// Positive = towards +Y (clockwise on screen). Returns 0 if either vector is zero.
    /// </summary>
    public static double SignedAngleBetween(Vector2D from, Vector2D to)
    {
        if (from.LengthSquared == 0 || to.LengthSquared == 0)
            return 0.0;
        // atan2(cross, dot) is accurate at every angle, unlike acos(dot), which loses precision near 0° and 180°.
        return NormalizeSignedAngle(RadiansToDegrees(Math.Atan2(from.Cross(to), from.Dot(to))));
    }
}
