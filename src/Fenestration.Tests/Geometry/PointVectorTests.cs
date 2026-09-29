using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class Point2DTests
{
    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new Point2D(100, 200);
        var b = new Point2D(100, 200);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new Point2D(100, 200.0001));
    }

    [Fact]
    public void Equality_IsSafeAsHashKey()
    {
        var set = new HashSet<Point2D> { new(1.5, 2.5), new(1.5, 2.5), new(0.0, 0.0), new(-0.0, 0.0) };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void AlmostEquals_UsesTolerance()
    {
        var a = new Point2D(100, 200);
        Assert.True(a.AlmostEquals(new Point2D(100.05, 200)));             // within 0.1 mm default
        Assert.False(a.AlmostEquals(new Point2D(100.2, 200)));
        Assert.True(a.AlmostEquals(new Point2D(100.2, 200), tolerance: 0.5));
    }

    [Fact]
    public void Distance_And_DistanceSquared()
    {
        var a = new Point2D(0, 0);
        var b = new Point2D(300, 400);
        Near(500.0, a.DistanceTo(b));
        Near(250_000.0, a.DistanceSquaredTo(b));
        Near(0.0, b.DistanceTo(b));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.5, 600.0, 750.0)]
    [InlineData(1.0, 1200.0, 1500.0)]
    [InlineData(2.0, 2400.0, 3000.0)]   // extrapolates
    public void Lerp(double t, double x, double y)
    {
        Near(new Point2D(x, y), Point2D.Origin.Lerp(new Point2D(1200, 1500), t));
    }

    [Fact]
    public void PointPlusVector_And_PointMinusPoint()
    {
        Near(new Point2D(11, 22), new Point2D(10, 20) + new Vector2D(1, 2));
        Near(new Point2D(9, 18), new Point2D(10, 20) - new Vector2D(1, 2));
        Near(new Vector2D(6, 15), new Point2D(10, 20) - new Point2D(4, 5));
    }

    [Fact]
    public void IsFinite_DetectsNaNAndInfinity()
    {
        Assert.True(new Point2D(1, 2).IsFinite);
        Assert.False(new Point2D(double.NaN, 2).IsFinite);
        Assert.False(new Point2D(1, double.PositiveInfinity).IsFinite);
    }

    [Fact]
    public void ToString_IsCultureInvariant()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("(1.5, 2.25)", new Point2D(1.5, 2.25).ToString());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}

public class Vector2DTests
{
    [Fact]
    public void Addition_Subtraction_Negation()
    {
        var a = new Vector2D(1, 2);
        var b = new Vector2D(3, 5);
        Near(new Vector2D(4, 7), a + b);
        Near(new Vector2D(-2, -3), a - b);
        Near(new Vector2D(-1, -2), -a);
    }

    [Fact]
    public void Scaling_And_Division()
    {
        var a = new Vector2D(1, 2);
        Near(new Vector2D(2.5, 5), a * 2.5);
        Near(new Vector2D(2.5, 5), 2.5 * a);
        Near(new Vector2D(0.5, 1), a / 2);
    }

    [Fact]
    public void Division_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => new Vector2D(1, 2) / 0);
        Assert.Throws<DivideByZeroException>(() => new Vector2D(1, 2) / double.NaN);
    }

    [Fact]
    public void Length_And_LengthSquared()
    {
        Near(5.0, new Vector2D(3, 4).Length);
        Near(25.0, new Vector2D(3, 4).LengthSquared);
    }

    [Fact]
    public void Normalized_HasUnitLength_AndSameDirection()
    {
        var n = new Vector2D(300, 400).Normalized();
        Near(1.0, n.Length);
        Near(new Vector2D(0.6, 0.8), n);
    }

    [Fact]
    public void Normalized_ZeroVector_IsZero_NotNaN()
    {
        var n = Vector2D.Zero.Normalized();
        Assert.Equal(Vector2D.Zero, n);
        Assert.True(n.IsFinite);
    }

    [Fact]
    public void Dot()
    {
        Near(0.0, Vector2D.UnitX.Dot(Vector2D.UnitY));
        Near(11.0, new Vector2D(1, 2).Dot(new Vector2D(3, 4)));
        Near(-1.0, Vector2D.UnitX.Dot(-Vector2D.UnitX));
    }

    [Fact]
    public void Cross_SignFollowsYDownConvention()
    {
        // +X → +Y is a clockwise quarter-turn on screen and gives a positive cross product.
        Near(1.0, Vector2D.UnitX.Cross(Vector2D.UnitY));
        Near(-1.0, Vector2D.UnitY.Cross(Vector2D.UnitX));
        Near(0.0, new Vector2D(2, 4).Cross(new Vector2D(1, 2))); // parallel
        Near(-2.0, new Vector2D(1, 2).Cross(new Vector2D(3, 4)));
    }

    [Fact]
    public void Perpendicular_IsOrthogonal()
    {
        var v = new Vector2D(3, 7);
        Near(0.0, v.Dot(v.Perpendicular()));
        Near(new Vector2D(0, 1), Vector2D.UnitX.Perpendicular());
    }
}
