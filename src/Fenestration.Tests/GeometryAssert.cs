using Fenestration.Core.Geometry;
using Xunit;

namespace Fenestration.Tests;

/// <summary>Tolerance-based assertions for floating-point geometry.</summary>
internal static class GeometryAssert
{
    /// <summary>Numerical tolerance for results that should be exact up to round-off.</summary>
    public const double Tolerance = 1e-9;

    public static void Near(double expected, double actual, double tolerance = Tolerance)
        => Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} ± {tolerance}, got {actual}.");

    public static void Near(Point2D expected, Point2D actual, double tolerance = Tolerance)
        => Assert.True(expected.AlmostEquals(actual, tolerance),
            $"Expected {expected} ± {tolerance}, got {actual}.");

    public static void Near(Vector2D expected, Vector2D actual, double tolerance = Tolerance)
        => Assert.True(expected.AlmostEquals(actual, tolerance),
            $"Expected {expected} ± {tolerance}, got {actual}.");

    public static void Near(Rectangle2D expected, Rectangle2D actual, double tolerance = Tolerance)
        => Assert.True(expected.AlmostEquals(actual, tolerance),
            $"Expected {expected} ± {tolerance}, got {actual}.");
}
