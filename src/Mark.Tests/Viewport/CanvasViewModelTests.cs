using Mark.Core.Geometry;
using Mark.Core.Viewport;
using Mark.Designer.Rendering;
using Mark.Designer.ViewModels;
using Xunit;
using static Mark.Tests.GeometryAssert;

namespace Mark.Tests.Viewport;

/// <summary>Viewport navigation as the UI drives it: wheel zoom, drag pan, fit, reset, cursor readout.</summary>
public class CanvasViewModelTests
{
    private const double ViewportWidth = 800;
    private const double ViewportHeight = 600;
    private static readonly Rectangle2D TestRectangle = new(0, 0, 1200, 1500);

    /// <summary>A sized viewport at 100 % zoom with pan (0, 0), and no content.</summary>
    private static CanvasViewModel CreateViewport(ViewportSettings? settings = null)
    {
        var vm = new CanvasViewModel(settings);
        vm.SetViewportSize(ViewportWidth, ViewportHeight);
        vm.ZoomLevel = 1.0;
        vm.PanX = 0;
        vm.PanY = 0;
        return vm;
    }

    // â”€â”€ World â†” screen â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(1200, 1500)]
    public void WorldToScreen_KnownPoints(double x, double y)
    {
        var vm = CreateViewport();
        vm.ZoomLevel = 0.5;
        vm.PanX = -100;
        vm.PanY = -40;

        Point2D screen = vm.WorldToScreen(new Point2D(x, y));

        Near(new Point2D((x + 100) * 0.5, (y + 40) * 0.5), screen);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(1200, 1500)]
    public void ScreenToWorld_IsInverse_WithinGeometryTolerance(double x, double y)
    {
        var vm = CreateViewport();
        vm.ZoomAtWheel(new Point2D(321, 123), 360);  // arbitrary non-trivial view
        vm.Pan(-57, 88);

        var world = new Point2D(x, y);
        Point2D roundTrip = vm.ScreenToWorld(vm.WorldToScreen(world));

        Assert.True(world.AlmostEquals(roundTrip, GeometryTolerance.Default));
        Near(world, roundTrip, tolerance: 1e-9);
    }

    // â”€â”€ Zoom â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Theory]
    [InlineData(120)]
    [InlineData(-120)]
    [InlineData(600)]
    [InlineData(-40)]    // precision touchpad
    public void WheelZoom_KeepsWorldPointUnderCursor(int wheelDelta)
    {
        var vm = CreateViewport();
        var cursor = new Point2D(512, 287);
        Point2D before = vm.ScreenToWorld(cursor);

        vm.ZoomAtWheel(cursor, wheelDelta);

        Near(before, vm.ScreenToWorld(cursor), tolerance: 1e-9);
        Near(Math.Pow(vm.Settings.ZoomFactor, wheelDelta / 120.0), vm.ZoomLevel);
    }

    [Fact]
    public void WheelUp_ZoomsIn_WheelDown_ZoomsOut()
    {
        var vm = CreateViewport();
        vm.ZoomAtWheel(new Point2D(10, 10), 120);
        Assert.True(vm.ZoomLevel > 1.0);
        vm.ZoomAtWheel(new Point2D(10, 10), -240);
        Assert.True(vm.ZoomLevel < 1.0);
    }

    [Fact]
    public void ZoomIn_ZoomOut_AroundViewportCenter()
    {
        var vm = CreateViewport();
        var center = new Point2D(ViewportWidth / 2, ViewportHeight / 2);
        Point2D before = vm.ScreenToWorld(center);

        vm.ZoomIn();
        Near(1.15, vm.ZoomLevel);
        Near(before, vm.ScreenToWorld(center));

        vm.ZoomOut();
        Near(1.0, vm.ZoomLevel);
        Near(before, vm.ScreenToWorld(center));
    }

    [Fact]
    public void ZoomLimits_ComeFromSettings()
    {
        var vm = CreateViewport(new ViewportSettings { MinZoom = 0.05, MaxZoom = 50 });

        for (int i = 0; i < 200; i++) vm.ZoomIn();
        Assert.Equal(50.0, vm.ZoomLevel);

        for (int i = 0; i < 400; i++) vm.ZoomOut();
        Assert.Equal(0.05, vm.ZoomLevel);
        Assert.Equal("5%", vm.ZoomPercentage);
    }

    [Fact]
    public void ZoomAtLimit_StillKeepsCursorFixed()
    {
        var vm = CreateViewport();
        var cursor = new Point2D(250, 400);
        Point2D before = vm.ScreenToWorld(cursor);

        vm.ZoomAt(cursor, 1e6);

        Assert.Equal(vm.Settings.MaxZoom, vm.ZoomLevel);
        Near(before, vm.ScreenToWorld(cursor), tolerance: 1e-9);
    }

    // â”€â”€ Pan â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Pan_ShiftsScreenPositionsByTheDragDelta()
    {
        var vm = CreateViewport();
        vm.ZoomLevel = 0.5;
        var world = new Point2D(600, 750);
        Point2D before = vm.WorldToScreen(world);

        vm.Pan(120, -30);

        Near(new Vector2D(120, -30), vm.WorldToScreen(world) - before);
        Near(-240.0, vm.PanX);   // 120 px at 0.5 px/mm = 240 mm
        Near(60.0, vm.PanY);
    }

    [Fact]
    public void Pan_DoesNotChangeZoomOrContent()
    {
        var vm = CreateViewport();
        var layer = new TestRectangleLayer(TestRectangle);
        vm.ContentLayers.Add(layer);

        vm.Pan(500, 500);

        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.Equal(TestRectangle, layer.Rectangle);
    }

    // â”€â”€ Fit & reset â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void FitToBounds_ContentFitsInsideViewportWithMargin()
    {
        var vm = CreateViewport();
        vm.FitToBounds(TestRectangle.Bounds);

        Point2D topLeft = vm.WorldToScreen(TestRectangle.TopLeft);
        Point2D bottomRight = vm.WorldToScreen(TestRectangle.BottomRight);
        double margin = vm.Settings.FitMarginFraction;

        // Height limits: 600 Ã— 0.9 / 1500
        Near(0.36, vm.ZoomLevel);
        Assert.InRange(topLeft.X, 0, ViewportWidth);
        Assert.InRange(bottomRight.X, 0, ViewportWidth);
        Near(ViewportHeight * margin / 2, topLeft.Y, tolerance: 1e-6);
        Near(ViewportHeight * (1 - margin / 2), bottomRight.Y, tolerance: 1e-6);
        Near(new Point2D(ViewportWidth / 2, ViewportHeight / 2), vm.WorldToScreen(TestRectangle.Center), tolerance: 1e-6);
    }

    [Fact]
    public void FitToContent_UsesLayerBounds()
    {
        var vm = CreateViewport();
        vm.ContentLayers.Add(new TestRectangleLayer(TestRectangle));
        vm.ContentLayers.Add(new TestRectangleLayer(new Rectangle2D(1500, 0, 900, 1500)));

        Assert.Equal(new Rectangle2D(0, 0, 2400, 1500), vm.ContentBounds.ToRectangle());

        vm.FitToContent();
        Near(new Point2D(ViewportWidth / 2, ViewportHeight / 2), vm.WorldToScreen(new Point2D(1200, 750)), tolerance: 1e-6);
    }

    [Fact]
    public void FitToContent_WithNoContent_Resets()
    {
        var vm = CreateViewport();
        vm.ZoomLevel = 3;
        vm.FitToContent();
        Near(1.0, vm.ZoomLevel);
    }

    [Fact]
    public void ResetView_IsPredictable()
    {
        var vm = CreateViewport();
        vm.ZoomAtWheel(new Point2D(100, 100), 480);
        vm.Pan(300, -200);

        vm.ResetView();

        double margin = vm.Settings.ResetOriginMarginPixels;
        Near(1.0, vm.ZoomLevel);
        Near(new Point2D(margin, margin), vm.WorldToScreen(Point2D.Origin));
    }

    [Fact]
    public void FirstSizing_FitsContent()
    {
        var vm = new CanvasViewModel();
        vm.ContentLayers.Add(new TestRectangleLayer(TestRectangle));

        vm.SetViewportSize(ViewportWidth, ViewportHeight);

        Near(0.36, vm.ZoomLevel);
    }

    // â”€â”€ Cursor readout â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Cursor_IsDerivedThroughScreenToWorld()
    {
        var vm = CreateViewport();
        vm.ZoomLevel = 0.5;
        vm.PanX = 100;
        vm.PanY = 50;

        vm.UpdateCursor(new Point2D(575.2, 346.9));

        Assert.NotNull(vm.CursorWorldPosition);
        Near(new Point2D(1250.4, 743.8), vm.CursorWorldPosition!.Value, tolerance: 1e-9);
        Assert.Equal("X: 1250.4 mm   Y: 743.8 mm", vm.CursorText);

        vm.ClearCursor();
        Assert.Null(vm.CursorWorldPosition);
    }

    // â”€â”€ Change notification â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ViewChanged_RaisedByNavigation_ContentChanged_ByLayers()
    {
        var vm = CreateViewport();
        int viewChanges = 0, contentChanges = 0;
        vm.ViewChanged += () => viewChanges++;
        vm.ContentChanged += () => contentChanges++;

        vm.ZoomIn();
        vm.Pan(1, 1);
        vm.ShowGrid = false;
        vm.ContentLayers.Add(new TestRectangleLayer(TestRectangle));
        vm.UpdateCursor(new Point2D(5, 5));   // must not trigger a redraw

        Assert.Equal(3, viewChanges);
        Assert.Equal(1, contentChanges);
    }
}
