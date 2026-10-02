using Mark.Core.Geometry;
using Mark.Core.Models;
using Xunit;

namespace Mark.Tests.Models;

public class ModelTests
{
    private const int Precision = 9;

    [Fact]
    public void VerticalProfile_LengthAndAngle()
    {
        var profile = new Profile
        {
            ProfileType = ProfileType.Mullion,
            StartPoint = new Point2D(0, 0),
            EndPoint = new Point2D(0, 1500)
        };

        Assert.Equal(1500.0, profile.Length, Precision);
        Assert.Equal(90.0, profile.Angle, Precision); // Y is down, so ↓ is +90°
    }

    [Fact]
    public void HorizontalProfile_LengthAndAngle()
    {
        var profile = new Profile
        {
            ProfileType = ProfileType.Transom,
            StartPoint = new Point2D(0, 0),
            EndPoint = new Point2D(1200, 0)
        };

        Assert.Equal(1200.0, profile.Length, Precision);
        Assert.Equal(0.0, profile.Angle, Precision);
    }

    [Fact]
    public void Profile_Create_Valid()
    {
        var profile = Profile.Create(ProfileType.Mullion, new Point2D(600, 0), new Point2D(600, 1500), 60);
        Assert.Equal(ProfileType.Mullion, profile.ProfileType);
        Assert.Equal(1500.0, profile.Length, Precision);
        Assert.Equal(60.0, profile.Thickness);
    }

    [Fact]
    public void Profile_Create_RejectsZeroLength()
    {
        Assert.Throws<ArgumentException>(() =>
            Profile.Create(ProfileType.Mullion, new Point2D(600, 0), new Point2D(600, 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void Profile_Create_RejectsInvalidThickness(double thickness)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Profile.Create(ProfileType.Transom, new Point2D(0, 900), new Point2D(1200, 900), thickness));
    }

    [Fact]
    public void Profile_Create_RejectsNaNPoint()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Profile.Create(ProfileType.Generic, new Point2D(double.NaN, 0), new Point2D(1200, 0)));
    }

    [Fact]
    public void Profile_Bounds_IncludeThickness()
    {
        var profile = new Profile
        {
            StartPoint = new Point2D(600, 0),
            EndPoint = new Point2D(600, 1500),
            Thickness = 60
        };

        Assert.Equal(new Rectangle2D(570, -30, 60, 1560), profile.GetBounds());
    }

    [Fact]
    public void Frame_Create_SetsBounds()
    {
        var frame = Frame.Create(0, 0, 1200, 1500);
        Assert.Equal(new Rectangle2D(0, 0, 1200, 1500), frame.Bounds);
        Assert.Equal(new Point2D(600, 750), frame.Center);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(-1200, 1500)]
    [InlineData(1200, 0)]
    [InlineData(1200, double.NaN)]
    [InlineData(1200, 1e9)]
    public void Frame_Create_RejectsInvalidDimensions(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame.Create(0, 0, width, height));
    }

    [Fact]
    public void Dimension_ValueIsDerivedFromGeometry()
    {
        var horizontal = new Dimension
        {
            StartPoint = new Point2D(0, -100),
            EndPoint = new Point2D(1200, -80),
            Orientation = DimensionOrientation.Horizontal
        };
        var vertical = new Dimension
        {
            StartPoint = new Point2D(-100, 0),
            EndPoint = new Point2D(-100, 1500),
            Orientation = DimensionOrientation.Vertical
        };

        Assert.Equal(1200.0, horizontal.Value, Precision);
        Assert.Equal(1500.0, vertical.Value, Precision);
    }
}
