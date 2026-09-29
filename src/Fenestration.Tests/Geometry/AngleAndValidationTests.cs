using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class AngleMathTests
{
    [Fact]
    public void DegreesRadiansRoundTrip()
    {
        Near(Math.PI, AngleMath.DegreesToRadians(180));
        Near(90.0, AngleMath.RadiansToDegrees(Math.PI / 2));
        Near(37.5, AngleMath.RadiansToDegrees(AngleMath.DegreesToRadians(37.5)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-720, 0)]
    [InlineData(-1e-15, 0)]
    public void NormalizeAngle_WrapsInto0To360(double input, double expected)
    {
        double result = AngleMath.NormalizeAngle(input);
        Near(expected, result);
        Assert.InRange(result, 0.0, 359.999999999);
    }

    [Theory]
    [InlineData(270, -90)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    [InlineData(190, -170)]
    public void NormalizeSignedAngle(double input, double expected)
    {
        Near(expected, AngleMath.NormalizeSignedAngle(input));
    }

    [Fact]
    public void NormalizeAngle_RejectsNaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleMath.NormalizeAngle(double.NaN));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 90)]      // down on screen
    [InlineData(-1, 0, 180)]
    [InlineData(0, -1, 270)]    // up on screen
    [InlineData(1, 1, 45)]
    public void DirectionAngle(double x, double y, double expected)
    {
        Near(expected, AngleMath.DirectionAngle(new Vector2D(x, y)));
    }

    [Fact]
    public void AngleBetween_And_Signed()
    {
        Near(90.0, AngleMath.AngleBetween(Vector2D.UnitX, Vector2D.UnitY));
        Near(90.0, AngleMath.SignedAngleBetween(Vector2D.UnitX, Vector2D.UnitY));
        Near(-90.0, AngleMath.SignedAngleBetween(Vector2D.UnitY, Vector2D.UnitX));
        Near(180.0, AngleMath.AngleBetween(Vector2D.UnitX, -Vector2D.UnitX));
        Near(0.0, AngleMath.AngleBetween(new Vector2D(3, 3), new Vector2D(1, 1)));
    }

    [Fact]
    public void ZeroVectors_GiveZero_NotNaN()
    {
        Assert.Equal(0.0, AngleMath.DirectionAngle(Vector2D.Zero));
        Assert.Equal(0.0, AngleMath.AngleBetween(Vector2D.Zero, Vector2D.UnitX));
    }
}

public class GeometryValidationTests
{
    [Fact]
    public void EnsureFinite_RejectsNaNAndInfinity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureFinite(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureFinite(double.NegativeInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureFinite(new Point2D(0, double.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureFinite(new Vector2D(double.PositiveInfinity, 0)));
        GeometryValidation.EnsureFinite(12.5);
    }

    [Fact]
    public void EnsureFinite_ReportsArgumentName()
    {
        double frameWidth = double.NaN;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureFinite(frameWidth));
        Assert.Equal(nameof(frameWidth), ex.ParamName);
    }

    [Fact]
    public void EnsureNonNegative_And_Positive()
    {
        GeometryValidation.EnsureNonNegative(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureNonNegative(-0.001));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsurePositive(0));
    }

    [Fact]
    public void Tolerance_MustBeFiniteAndNonNegative()
    {
        Assert.True(GeometryValidation.IsValidTolerance(0));
        Assert.True(GeometryValidation.IsValidTolerance(GeometryTolerance.Default));
        Assert.False(GeometryValidation.IsValidTolerance(-0.1));
        Assert.False(GeometryValidation.IsValidTolerance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryValidation.EnsureValidTolerance(-1));
    }

    [Fact]
    public void EnsureNonDegenerate_RejectsZeroLength()
    {
        Assert.Throws<ArgumentException>(() =>
            GeometryValidation.EnsureNonDegenerate(new LineSegment2D(100, 100, 100, 100)));
        Assert.Throws<ArgumentException>(() =>
            GeometryValidation.EnsureNonDegenerate(new LineSegment2D(100, 100, 100.05, 100)));
        GeometryValidation.EnsureNonDegenerate(new LineSegment2D(0, 0, 0, 1500));
    }

    [Fact]
    public void GeometryTolerance_Helpers()
    {
        Assert.True(GeometryTolerance.AreEqual(1200.0, 1200.05));
        Assert.False(GeometryTolerance.AreEqual(1200.0, 1200.2));
        Assert.True(GeometryTolerance.IsZero(-0.05));
    }
}
