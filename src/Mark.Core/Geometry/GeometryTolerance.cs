namespace Mark.Core.Geometry;

/// <summary>
/// Central home for every geometric tolerance. Geometry code must not contain ad-hoc
/// literals like 0.001 or 0.01 — it uses these values (or a caller-supplied tolerance).
/// Change a value here to change it everywhere.
/// </summary>
public static class GeometryTolerance
{
    /// <summary>
    /// Modelling tolerance in millimetres (0.1 mm).
    /// Two points closer than this are treated as the same point when making design decisions:
    /// "is this point on that segment?", "do these segments touch?", "are these lines collinear?".
    /// It is well below fabrication accuracy (saw cuts and assembly are typically ±0.5 mm),
    /// so it never merges geometry a fabricator would consider distinct, yet it is large enough
    /// to absorb floating-point drift from repeated moves, rotations and unit conversions.
    /// </summary>
    public const double Default = 0.1;

    /// <summary>
    /// Pure numerical epsilon. Guards divisions and zero-length vectors against round-off.
    /// It is NOT a design tolerance and should not be used to decide whether geometry coincides.
    /// </summary>
    public const double Epsilon = 1e-9;

    /// <summary>True if <paramref name="a"/> and <paramref name="b"/> differ by at most <paramref name="tolerance"/>.</summary>
    public static bool AreEqual(double a, double b, double tolerance = Default)
        => Math.Abs(a - b) <= tolerance;

    /// <summary>True if <paramref name="value"/> is within <paramref name="tolerance"/> of zero.</summary>
    public static bool IsZero(double value, double tolerance = Default)
        => Math.Abs(value) <= tolerance;
}
