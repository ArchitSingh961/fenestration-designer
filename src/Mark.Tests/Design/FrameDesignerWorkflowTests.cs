using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;
using Mark.Designer.Interaction;
using Mark.Designer.Tools;
using Mark.Designer.ViewModels;
using Xunit;
using static Mark.Tests.GeometryAssert;

namespace Mark.Tests.Design;

/// <summary>
/// The milestone's definition-of-done workflow, driven through the real view models and select tool
/// (no window): create → divide → select → edit → drag → resize → undo.
/// </summary>
public class FrameDesignerWorkflowTests
{
    private static MainViewModel CreateApp()
    {
        var vm = new MainViewModel();
        vm.Canvas.SetViewportSize(1000, 800);
        return vm;
    }

    private static Frame OnlyFrame(MainViewModel vm) => Assert.Single(vm.Project.Frames);

    private static Profile Single(Frame frame, ProfileType type) => frame.Profiles.Single(p => p.ProfileType == type);

    private static ViewportPointerEventArgs PointerAtWorld(MainViewModel vm, Point2D world, bool ctrl = false)
        => new(vm.Canvas.WorldToScreen(world), world, ctrl);

    private static ViewportPointerEventArgs PointerAtScreen(MainViewModel vm, Point2D screen)
        => new(screen, vm.Canvas.ScreenToWorld(screen), false);

    [Fact]
    public void DefinitionOfDone_Workflow()
    {
        var vm = CreateApp();

        // Create a 1200 × 1500 frame.
        vm.NewFrameWidthText = "1200";
        vm.NewFrameHeightText = "1500";
        vm.CreateFrameCommand.Execute(null);
        var frame = OnlyFrame(vm);
        Assert.Equal(1200.0, frame.Width);
        Assert.Equal(1500.0, frame.Height);
        Assert.True(vm.IsSelected(frame.Id));
        Assert.Equal("FRAME", vm.Properties.Header);

        // Add a mullion and position it at 600 via the properties panel.
        vm.AddMullionCommand.Execute(null);
        var mullion = Single(frame, ProfileType.Mullion);
        Assert.True(vm.IsSelected(mullion.Id));
        Assert.Equal("MULLION", vm.Properties.Header);
        vm.Properties.PositionText = "600";
        vm.Properties.ApplyPositionCommand.Execute(null);
        Assert.Null(vm.Properties.ErrorMessage);

        // Add a transom (whole frame, since a mullion is selected) and position it at 750.
        vm.AddTransomCommand.Execute(null);
        var transom = Single(frame, ProfileType.Transom);
        vm.Properties.PositionText = "750";
        vm.Properties.ApplyPositionCommand.Execute(null);

        Assert.Equal(4, frame.GlassPanels.Count);
        Assert.Equal(600.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
        Assert.Equal(750.0, Members.DivisionPosition(Single(frame, ProfileType.Transom)));

        // Select glass by clicking it; its properties show the derived size.
        vm.ActiveTool.OnPointerDown(PointerAtWorld(vm, new Point2D(300, 300)));
        vm.ActiveTool.OnPointerUp(PointerAtWorld(vm, new Point2D(300, 300)));
        Assert.Equal("GLASS", vm.Properties.Header);
        Assert.Contains(vm.Properties.Items, i => i.Name == "Width" && i.Value == "510");

        // Move the mullion by editing its position: glass updates.
        vm.Select(mullion.Id);
        vm.Properties.PositionText = "700";
        vm.Properties.ApplyPositionCommand.Execute(null);
        var widths = frame.GlassPanels.Where(g => g.Boundary.Top < 700).OrderBy(g => g.Boundary.Left).Select(g => g.Boundary.Width);
        Assert.Equal(new[] { 610.0, 410.0 }, widths);

        // Resize the frame: glass and dimensions update.
        vm.Select(frame.Id);
        vm.Properties.WidthText = "1400";
        vm.Properties.HeightText = "1600";
        vm.Properties.ApplyFrameSizeCommand.Execute(null);
        Assert.Null(vm.Properties.ErrorMessage);
        Assert.Equal(1400.0, frame.Width);
        Assert.Equal(1400.0, AutoDimensions.Compute(frame).Single(d => d.Side == DimensionSide.Top).Value);
        Assert.Equal(4, frame.GlassPanels.Count);

        // Everything is undoable, back to the empty project.
        while (vm.CommandHistory.CanUndo) vm.CommandHistory.Undo();
        Assert.Empty(vm.Project.Frames);
    }

    [Fact]
    public void InvalidFrameSize_ShowsMessage_AndCreatesNothing()
    {
        var vm = CreateApp();
        vm.NewFrameWidthText = "-500";
        vm.CreateFrameCommand.Execute(null);

        Assert.Empty(vm.Project.Frames);
        Assert.True(vm.HasDesignMessage);
        Assert.Contains("Width", vm.DesignMessage);
    }

    [Fact]
    public void InvalidResize_ShowsMessage_AndKeepsFrame()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);

        vm.Select(frame.Id);
        vm.Properties.WidthText = "500";
        vm.Properties.ApplyFrameSizeCommand.Execute(null);

        Assert.NotNull(vm.Properties.ErrorMessage);
        Assert.Equal(1200.0, frame.Width);
    }

    [Fact]
    public void AddTransom_WithGlassSelected_SplitsOnlyThatGlass()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);
        var leftGlass = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();

        vm.Select(leftGlass.Id);
        vm.AddTransomCommand.Execute(null);

        Assert.Equal(3, frame.GlassPanels.Count);
        var transom = Single(frame, ProfileType.Transom);
        Assert.Equal(600.0, transom.EndPoint.X);   // ends on the mullion
    }

    [Fact]
    public void AddDivision_WithoutFrame_ExplainsWhy()
    {
        var vm = CreateApp();
        vm.AddMullionCommand.Execute(null);
        Assert.Equal("Create a frame first.", vm.DesignMessage);
    }

    [Fact]
    public void DeleteGlass_IsRefused_DeleteDivision_Works()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);

        vm.Select(frame.GlassPanels[0].Id);
        vm.DeleteSelectedCommand.Execute(null);
        Assert.Equal(2, frame.GlassPanels.Count);
        Assert.Contains("Glass is created from the frame layout", vm.DesignMessage);

        vm.Select(Single(frame, ProfileType.Mullion).Id);
        vm.DeleteSelectedCommand.Execute(null);
        Assert.Single(frame.GlassPanels);
    }

    // ── Select tool (mouse) ─────────────────────────────────────────

    [Fact]
    public void Click_SelectsByWorldGeometry_AtAnyZoom()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);
        var mullion = Single(frame, ProfileType.Mullion);

        foreach (double zoom in new[] { 0.1, 0.5, 2.0 })
        {
            vm.Canvas.ZoomLevel = zoom;
            vm.ClearSelection();
            vm.ActiveTool.OnPointerDown(PointerAtWorld(vm, new Point2D(600, 400)));
            vm.ActiveTool.OnPointerUp(PointerAtWorld(vm, new Point2D(600, 400)));
            Assert.True(vm.IsSelected(mullion.Id), $"zoom {zoom}");
        }

        vm.ActiveTool.OnPointerDown(PointerAtWorld(vm, new Point2D(20, 700)));
        Assert.True(vm.IsSelected(frame.Id));

        vm.ActiveTool.OnPointerDown(PointerAtWorld(vm, new Point2D(-500, -500)));
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void DragMullion_MovesInWorldMillimetres_AsOneUndoStep()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);
        int undoBefore = vm.CommandHistory.UndoCount;

        vm.Canvas.ZoomLevel = 0.5;   // 1 px = 2 mm
        var start = vm.Canvas.WorldToScreen(new Point2D(600, 400));

        vm.ActiveTool.OnPointerDown(PointerAtScreen(vm, start));
        for (int i = 1; i <= 5; i++)
            vm.ActiveTool.OnPointerMove(PointerAtScreen(vm, new Point2D(start.X + 10 * i, start.Y)));
        vm.ActiveTool.OnPointerUp(PointerAtScreen(vm, new Point2D(start.X + 50, start.Y)));

        // 50 px at 0.5 px/mm = 100 mm.
        Assert.Equal(700.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
        Assert.Equal(undoBefore + 1, vm.CommandHistory.UndoCount);

        vm.CommandHistory.Undo();
        Assert.Equal(600.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
    }

    [Fact]
    public void DragMullion_StopsAtTheLastValidPosition()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);
        vm.Canvas.ZoomLevel = 1.0;
        var start = vm.Canvas.WorldToScreen(new Point2D(600, 400));

        vm.ActiveTool.OnPointerDown(PointerAtScreen(vm, start));
        vm.ActiveTool.OnPointerMove(PointerAtScreen(vm, new Point2D(start.X + 2000, start.Y)));   // far past the frame
        vm.ActiveTool.OnPointerUp(PointerAtScreen(vm, new Point2D(start.X + 2000, start.Y)));

        // Right frame inner face 1140 − 50 mm glass − 30 mm half mullion = 1060.
        Assert.Equal(1060.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);
    }

    [Fact]
    public void DragMullion_SnapsToFrameCentre()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        var frame = OnlyFrame(vm);
        vm.Select(frame.Id);
        vm.Properties.PositionText = "";
        vm.AddMullionCommand.Execute(null);
        vm.Properties.PositionText = "400";
        vm.Properties.ApplyPositionCommand.Execute(null);
        vm.Canvas.ZoomLevel = 1.0;

        var start = vm.Canvas.WorldToScreen(new Point2D(400, 400));
        vm.ActiveTool.OnPointerDown(PointerAtScreen(vm, start));
        vm.ActiveTool.OnPointerMove(PointerAtScreen(vm, new Point2D(start.X + 197, start.Y)));   // 597 mm, within 8 px of 600
        vm.ActiveTool.OnPointerUp(PointerAtScreen(vm, new Point2D(start.X + 197, start.Y)));

        Assert.Equal(600.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
    }

    [Fact]
    public void Escape_CancelsDrag()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        var frame = OnlyFrame(vm);
        int undoBefore = vm.CommandHistory.UndoCount;
        var start = vm.Canvas.WorldToScreen(new Point2D(600, 400));

        vm.ActiveTool.OnPointerDown(PointerAtScreen(vm, start));
        vm.ActiveTool.OnPointerMove(PointerAtScreen(vm, new Point2D(start.X + 60, start.Y)));
        Assert.True(vm.ActiveTool.OnKey(ViewportKey.Escape));

        Assert.Equal(600.0, Members.DivisionPosition(Single(frame, ProfileType.Mullion)));
        Assert.False(vm.ActiveTool.IsCapturing);
        Assert.Equal(undoBefore, vm.CommandHistory.UndoCount);
    }

    [Fact]
    public void TinyMouseJitter_IsAClickNotADrag()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        int undoBefore = vm.CommandHistory.UndoCount;
        var start = vm.Canvas.WorldToScreen(new Point2D(600, 400));

        vm.ActiveTool.OnPointerDown(PointerAtScreen(vm, start));
        vm.ActiveTool.OnPointerMove(PointerAtScreen(vm, new Point2D(start.X + 1, start.Y)));
        vm.ActiveTool.OnPointerUp(PointerAtScreen(vm, new Point2D(start.X + 1, start.Y)));

        Assert.Equal(undoBefore, vm.CommandHistory.UndoCount);
    }

    [Fact]
    public void Hover_ShowsResizeCursorOverDivisions()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.AddMullionCommand.Execute(null);
        vm.AddTransomCommand.Execute(null);

        var overMullion = PointerAtWorld(vm, new Point2D(600, 300));
        vm.ActiveTool.OnPointerMove(overMullion);
        Assert.Equal(ViewportCursor.ResizeHorizontal, overMullion.Cursor);

        var overTransom = PointerAtWorld(vm, new Point2D(300, 750));
        vm.ActiveTool.OnPointerMove(overTransom);
        Assert.Equal(ViewportCursor.ResizeVertical, overTransom.Cursor);

        var overGlass = PointerAtWorld(vm, new Point2D(300, 300));
        vm.ActiveTool.OnPointerMove(overGlass);
        Assert.Equal(ViewportCursor.Default, overGlass.Cursor);
    }

    [Fact]
    public void SecondFrame_IsPlacedToTheRight()
    {
        var vm = CreateApp();
        vm.CreateFrameCommand.Execute(null);
        vm.NewFrameWidthText = "900";
        vm.CreateFrameCommand.Execute(null);

        Assert.Equal(2, vm.Project.Frames.Count);
        Near(1200.0 + vm.Rules.FrameSpacingMm, vm.Project.Frames[1].X);
        Assert.True(vm.Canvas.ContentBounds.Contains(new Point2D(2500, 750)));
    }
}
