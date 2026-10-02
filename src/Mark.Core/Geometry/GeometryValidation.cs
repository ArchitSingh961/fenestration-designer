using System.Runtime.CompilerServices;

namespace Mark.Core.Geometry;

/// <summary>
/// Guards that stop invalid geometry (NaN, ±Infinity, negative sizes, degenerate segments,
/// negative tolerances) from entering the model. Each <c>Ensure*</c> method throws an
/// <see cref="ArgumentException"/> subtype; each <c>Is*</c> method is the non-throwing check.
/// </summary>
public static class GeometryValidation
{
    public static bool IsFinite(double value) => double.IsFinite(value);

    public static void EnsureFinite(double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be a finite number.");
    }

    public static void EnsureFinite(Point2D point, [CallerArgumentExpression(nameof(point))] string? paramName = null)
    {
        if (!point.IsFinite)
            throw new ArgumentOutOfRangeException(paramName, point, "Point coordinates must be finite numbers.");
    }

    public static void EnsureFinite(Vector2D vector, [CallerArgumentExpression(nameof(vector))] string? paramName = null)
    {
        if (!vector.IsFinite)
            throw new ArgumentOutOfRangeException(paramName, vector, "Vector components must be finite numbers.");
    }

    /// <summary>Requires a finite value ≥ 0 (e.g. a width or height, where zero is allowed).</summary>
    public static void EnsureNonNegative(double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be a finite, non-negative number.");
    }

    /// <summary>Requires a finite value &gt; 0.</summary>
    public static void EnsurePositive(double value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be a finite, positive number.");
    }

    public static bool IsValidTolerance(double tolerance) => double.IsFinite(tolerance) && tolerance >= 0;

    public static void EnsureValidTolerance(double tolerance, [CallerArgumentExpression(nameof(tolerance))] string? paramName = null)
    {
        if (!IsValidTolerance(tolerance))
            throw new ArgumentOutOfRangeException(paramName, tolerance, "Tolerance must be a finite, non-negative distance in mm.");
    }

    /// <summary>
    /// Requires a segment with finite endpoints that is longer than <paramref name="minLength"/>.
    /// Use where a zero-length element is meaningless (e.g. a profile).
    /// </summary>
    public static void EnsureNonDegenerate(
        LineSegment2D segment,
        double minLength = GeometryTolerance.Default,
        [CallerArgumentExpression(nameof(segment))] string? paramName = null)
    {
        EnsureFinite(segment.Start, paramName);
        EnsureFinite(segment.End, paramName);
        if (segment.IsDegenerate(minLength))
            throw new ArgumentException($"Segment must be longer than {minLength} mm (got {segment.Length} mm).", paramName);
    }
}
