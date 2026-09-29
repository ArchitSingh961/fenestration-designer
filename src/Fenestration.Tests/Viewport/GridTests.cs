using Fenestration.Core.Viewport;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Viewport;

public class GridSpacingTests
{
    private const double MinPixels = 12.0;

    [Theory]
    [InlineData(50.0, 1.0, 5.0)]      // 1 mm = 50 px → finest step
    [InlineData(1.0, 25.0, 100.0)]    // 100 %: 10 mm is only 10 px, so 25 mm
    [InlineData(0.36, 50.0, 250.0)]   // a 1200 × 1500 frame fitted to ~600 px
    [InlineData(0.05, 250.0, 1000.0)] // minimum zoom
    public void ForZoom_PicksFinestReadableStep(double zoom, double expectedMinor, double expectedMajor)
    {
        var spacing = GridSpacing.ForZoom(zoom, MinPixels);
        Near(expectedMinor, spacing.MinorMm);
        Near(expectedMajor, spacing.MajorMm);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    [InlineData(0.3)]
    [InlineData(1.0)]
    [InlineData(7.7)]
    [InlineData(50.0)]
    public void ForZoom_Invariants(double zoom)
    {
        var spacing = GridSpacing.ForZoom(zoom, MinPixels);
        Assert.True(spacing.MinorMm * zoom >= MinPixels, "minor lines must be at least the minimum pixel spacing apart");
        Assert.True(spacing.MajorEvery >= GridSpacing.MinMajorRatio);
        Near(spacing.MajorMm, spacing.MinorMm * spacing.MajorEvery);   // integer multiple
    }

    [Fact]
    public void ForZoom_BeyondLadder_KeepsGrowing()
    {
        var spacing = GridSpacing.ForZoom(0.0001, MinPixels);
        Assert.True(spacing.MinorMm * 0.0001 >= MinPixels);
    }

    [Fact]
    public void ForZoom_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSpacing.ForZoom(0, MinPixels));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridSpacing.ForZoom(double.NaN, MinPixels));
    }
}

public class GridAxisRangeTests
{
    [Fact]
    public void Visible_IncludesLinesOnTheBoundary()
    {
        var range = GridAxisRange.Visible(0, 1200, 100);
        Assert.Equal(0, range.FirstIndex);
        Assert.Equal(12, range.LastIndex);
        Assert.Equal(13, range.Count);
        Near(1200.0, range.PositionAt(range.LastIndex));
    }

    [Fact]
    public void Visible_HandlesNegativeWorldCoordinates()
    {
        var range = GridAxisRange.Visible(-130, 70, 50);
        Assert.Equal(-2, range.FirstIndex);   // -100
        Assert.Equal(1, range.LastIndex);     // 50
        Near(-100.0, range.PositionAt(range.FirstIndex));
    }

    [Fact]
    public void Visible_NoLinesInNarrowGap()
    {
        var range = GridAxisRange.Visible(101, 149, 50);
        Assert.Equal(0, range.Count);
    }

    [Fact]
    public void PositionAt_HasNoAccumulatedDrift()
    {
        var range = GridAxisRange.Visible(0, 1_000_000, 0.1);
        Near(1_000_000.0, range.PositionAt(range.LastIndex), tolerance: 1e-6);
    }
}

public class ViewportSettingsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var settings = new ViewportSettings();
        settings.Validate();
        Assert.Equal(0.05, settings.MinZoom);
        Assert.Equal(50.0, settings.MaxZoom);
        Assert.Equal(1.15, settings.ZoomFactor);
    }

    [Fact]
    public void Validate_RejectsBadValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewportSettings { MinZoom = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewportSettings { MinZoom = 10, MaxZoom = 5 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewportSettings { ZoomFactor = 1.0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewportSettings { FitMarginFraction = 1.0 }.Validate());
    }
}
