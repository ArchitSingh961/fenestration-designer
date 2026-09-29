using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class ViewportTransformTests
{
    [Fact]
    public void WorldToScreen_ZoomOne_SubtractsPan()
    {
        var vt = new ViewportTransform { Zoom = 1.0, PanX = 100, PanY = 50 };
        Near(new Point2D(1100, 1450), vt.WorldToScreen(new Point2D(1200, 1500)));
    }

    [Fact]
    public void WorldToScreen_AppliesPanThenZoom()
    {
        var vt = new ViewportTransform { Zoom = 0.5, PanX = 100, PanY = 200 };
        Near(new Point2D(600, 750), vt.WorldToScreen(new Point2D(1300, 1700)));
    }

    [Fact]
    public void ScreenToWorld()
    {
        var vt = new ViewportTransform { Zoom = 0.5, PanX = 100, PanY = 200 };
        Near(new Point2D(1300, 1700), vt.ScreenToWorld(new Point2D(600, 750)));
    }

    [Theory]
    [InlineData(1.0, 0, 0)]
    [InlineData(2.37, -45.5, 812)]
    [InlineData(0.013, 10_000, -3_000)]
    [InlineData(49.0, 599.5, 749.5)]
    public void RoundTrip_WorldScreenWorld(double zoom, double panX, double panY)
    {
        var vt = new ViewportTransform { Zoom = zoom, PanX = panX, PanY = panY };
        var world = new Point2D(500, 700);
        Near(world, vt.ScreenToWorld(vt.WorldToScreen(world)), tolerance: 1e-6);
    }

    [Fact]
    public void AffineTransform_MatchesDirectConversion()
    {
        var vt = new ViewportTransform { Zoom = 0.8, PanX = -120, PanY = 35 };
        var world = new Point2D(1200, 1500);
        Near(vt.WorldToScreen(world), vt.WorldToScreenTransform.TransformPoint(world));
        Near(world, vt.ScreenToWorldTransform.TransformPoint(vt.WorldToScreen(world)));
    }

    [Fact]
    public void Zoom_ScalesDistances()
    {
        var vt = new ViewportTransform { Zoom = 0.25 };
        Near(300.0, vt.WorldToScreenDistance(1200));
        Near(1200.0, vt.ScreenToWorldDistance(300));
    }

    [Fact]
    public void Zoom_IsClamped_AndRejectsNaN()
    {
        var vt = new ViewportTransform { Zoom = 1e6 };
        Assert.Equal(ViewportTransform.MaxZoom, vt.Zoom);
        vt.Zoom = 0;
        Assert.Equal(ViewportTransform.MinZoom, vt.Zoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => vt.Zoom = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => vt.PanX = double.PositiveInfinity);
    }

    [Fact]
    public void SetZoomLimits_ReclampsAndValidates()
    {
        var vt = new ViewportTransform { Zoom = 0.02 };
        vt.SetZoomLimits(0.05, 20);
        Assert.Equal(0.05, vt.Zoom);
        vt.Zoom = 100;
        Assert.Equal(20.0, vt.Zoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => vt.SetZoomLimits(5, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => vt.SetZoomLimits(0, 1));
    }

    [Fact]
    public void Pan_ShiftsView_NotWorldDistances()
    {
        var vt = new ViewportTransform { Zoom = 0.4 };
        Point2D before = vt.WorldToScreen(new Point2D(600, 750));
        vt.PanByScreenDelta(new Vector2D(123, -45));
        Point2D after = vt.WorldToScreen(new Point2D(600, 750));

        Near(new Vector2D(123, -45), after - before);   // content follows the drag
        Point2D a = vt.WorldToScreen(new Point2D(0, 0));
        Point2D b = vt.WorldToScreen(new Point2D(1200, 0));
        Near(1200 * 0.4, b.X - a.X);
    }

    [Theory]
    [InlineData(400, 300, 3.0)]
    [InlineData(0, 0, 0.2)]
    [InlineData(1024, 17, 12.5)]
    public void ZoomAt_KeepsWorldPointUnderCursorFixed(double sx, double sy, double newZoom)
    {
        var vt = new ViewportTransform { Zoom = 0.7, PanX = -150, PanY = 80 };
        var cursor = new Point2D(sx, sy);
        Point2D worldBefore = vt.ScreenToWorld(cursor);

        vt.ZoomAt(cursor, newZoom);

        Near(newZoom, vt.Zoom);
        Near(worldBefore, vt.ScreenToWorld(cursor), tolerance: 1e-6);
        Near(cursor, vt.WorldToScreen(worldBefore), tolerance: 1e-6);
    }

    [Fact]
    public void ZoomBy_MultipliesAroundCursor()
    {
        var vt = new ViewportTransform { Zoom = 1.0 };
        var cursor = new Point2D(400, 300);
        Point2D worldBefore = vt.ScreenToWorld(cursor);

        vt.ZoomBy(cursor, 1.15);

        Near(1.15, vt.Zoom);
        Near(worldBefore, vt.ScreenToWorld(cursor));
        Assert.Throws<ArgumentOutOfRangeException>(() => vt.ZoomBy(cursor, 0));
    }

    [Fact]
    public void ZoomAt_BeyondLimit_StillKeepsCursorFixed()
    {
        var vt = new ViewportTransform { Zoom = 1.0 };
        var cursor = new Point2D(200, 100);
        Point2D worldBefore = vt.ScreenToWorld(cursor);

        vt.ZoomAt(cursor, 1e9);

        Assert.Equal(ViewportTransform.MaxZoom, vt.Zoom);
        Near(worldBefore, vt.ScreenToWorld(cursor));
    }

    [Fact]
    public void VisibleWorldBounds()
    {
        var vt = new ViewportTransform { Zoom = 0.5, PanX = -100, PanY = -50 };
        Near(new Rectangle2D(-100, -50, 1600, 1200), vt.VisibleWorldBounds(800, 600));
    }

    [Fact]
    public void FitTo_CentresContentInViewport()
    {
        var content = BoundingBox2D.FromRectangle(new Rectangle2D(0, 0, 1200, 1500));
        var vt = new ViewportTransform();

        vt.FitTo(content, viewportWidth: 800, viewportHeight: 600, marginFraction: 0.1);

        // Height is the limiting axis: 600 / 1500 × 0.9
        Near(0.36, vt.Zoom);
        Near(new Point2D(400, 300), vt.WorldToScreen(content.Center));
    }

    [Fact]
    public void FitTo_EmptyContent_ResetsView()
    {
        var vt = new ViewportTransform { Zoom = 3, PanX = 10, PanY = 10 };
        vt.FitTo(BoundingBox2D.Empty, 800, 600);
        Near(ViewportTransform.DefaultZoom, vt.Zoom);
        Near(0.0, vt.PanX);
    }

    [Fact]
    public void FitTo_SinglePoint_StaysFinite()
    {
        var vt = new ViewportTransform();
        vt.FitTo(BoundingBox2D.FromPoints(new Point2D(600, 750)), 800, 600);
        Assert.True(double.IsFinite(vt.Zoom));
        Near(new Point2D(400, 300), vt.WorldToScreen(new Point2D(600, 750)));
    }
}
