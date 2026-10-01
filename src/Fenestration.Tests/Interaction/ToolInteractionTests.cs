using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Designer.Interaction;
using Fenestration.Designer.ViewModels;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Interaction;

/// <summary>
/// The interaction engine as the viewport drives it: tools receive pointer events (screen + world) and keys;
/// the model only changes through commands on release.
/// </summary>
public class ToolInteractionTests
{
    private static MainViewModel App(double zoom = 0.5)
    {
        var vm = new MainViewModel();
        vm.Canvas.SetViewportSize(1000, 800);
        vm.Canvas.ZoomLevel = zoom;
        vm.Canvas.PanX = -100;
        vm.Canvas.PanY = -100;
        return vm;
    }

    /// <summary>1200 × 1500 frame with a mullion at 600 and a transom at 750.</summary>
    private static (Frame Frame, Guid Mullion, Guid Transom) SampleDesign(MainViewModel vm)
    {
        vm.CreateFrameCommand.Execute(null);
        var frame = vm.Project.Frames.Single();
        vm.AddMullionCommand.Execute(null);
        vm.Properties.PositionText = "600";
        vm.Properties.ApplyPositionCommand.Execute(null);
        var mullion = frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).Id;
        vm.AddTransomCommand.Execute(null);
        vm.Properties.PositionText = "750";
        vm.Properties.ApplyPositionCommand.Execute(null);
        var transom = frame.Profiles.Single(p => p.ProfileType == ProfileType.Transom).Id;
        vm.ClearSelection();
        return (frame, mullion, transom);
    }

    private static ViewportPointerEventArgs At(MainViewModel vm, Point2D world, bool shift = false, bool ctrl = false)
        => new(vm.Canvas.WorldToScreen(world), world, ctrl, shift);

    private static void Click(MainViewModel vm, Point2D world, bool shift = false, bool ctrl = false)
    {
        vm.ActiveTool.OnPointerDown(At(vm, world, shift, ctrl));
        vm.ActiveTool.OnPointerUp(At(vm, world, shift, ctrl));
    }

    /// <summary>Drags in several steps, like a real mouse, optionally stopping before release.</summary>
    private static void Drag(MainViewModel vm, Point2D from, Point2D to, bool release = true, int steps = 5)
    {
        vm.ActiveTool.OnPointerDown(At(vm, from));
        for (int i = 1; i <= steps; i++)
            vm.ActiveTool.OnPointerMove(At(vm, new Point2D(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps)));
        if (release) vm.ActiveTool.OnPointerUp(At(vm, to));
    }

    private static double X(Frame frame, Guid id) => frame.Profiles.Single(p => p.Id == id).StartPoint.X;

    // ── Integration workflow (spec §51) ─────────────────────────────

    [Fact]
    public void Workflow_DragMullionTo700_UndoRedo()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);
        int historyBefore = vm.CommandHistory.UndoCount;

        Click(vm, new Point2D(600, 300));
        Assert.True(vm.IsSelected(mullion));

        Drag(vm, new Point2D(600, 300), new Point2D(700, 300));

        Assert.Equal(700.0, X(frame, mullion));
        Assert.Equal(historyBefore + 1, vm.CommandHistory.UndoCount);   // the whole drag is ONE command
        Assert.Equal(new[] { 610.0, 410.0 }, frame.GlassPanels.Where(g => g.Boundary.Top < 700).OrderBy(g => g.Boundary.Left).Select(g => g.Boundary.Width));
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);

        vm.UndoCommand.Execute(null);
        Assert.Equal(600.0, X(frame, mullion));
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);

        vm.RedoCommand.Execute(null);
        Assert.Equal(700.0, X(frame, mullion));
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);
    }

    [Fact]
    public void DuringDrag_OnlyThePreviewChanges()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);

        Drag(vm, new Point2D(600, 300), new Point2D(700, 300), release: false);

        Assert.Equal(600.0, X(frame, mullion));   // model untouched mid-drag
        Assert.Equal(700.0, X(vm.Interaction.Preview.ReplacementFrames[frame.Id], mullion));
        Assert.Equal("Mullion at 700 mm", vm.Hint);
        Assert.False(vm.HintIsError);
    }

    [Fact]
    public void Escape_CancelsDrag_RestoresAndRecordsNothing()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);
        int historyBefore = vm.CommandHistory.UndoCount;

        Drag(vm, new Point2D(600, 300), new Point2D(800, 300), release: false);
        Assert.True(vm.ActiveTool.OnKey(ViewportKey.Escape));

        Assert.Equal(600.0, X(frame, mullion));
        Assert.True(vm.Interaction.Preview.IsEmpty);
        Assert.False(vm.ActiveTool.IsCapturing);
        Assert.Equal(historyBefore, vm.CommandHistory.UndoCount);
    }

    [Fact]
    public void InvalidDrag_ShowsWarning_AndNeverCommitsInvalidGeometry()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);

        // 1110 leaves no glass between the mullion (face 1140) and the frame (face 1140).
        Drag(vm, new Point2D(600, 300), new Point2D(1110, 300), release: false);

        Assert.True(vm.HintIsError);
        Assert.Contains("minimum 50 mm", vm.Hint);
        Assert.False(vm.Interaction.Preview.IsValid);
        Assert.NotEmpty(vm.Interaction.Preview.Ghosts);

        vm.ActiveTool.OnPointerUp(At(vm, new Point2D(1110, 300)));
        Assert.Equal(1060.0, X(frame, mullion));   // the last valid position, not the invalid candidate
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);
    }

    [Fact]
    public void DragWithGridSnap()
    {
        var vm = App(zoom: 1.0);
        var (frame, mullion, _) = SampleDesign(vm);
        vm.ObjectSnapEnabled = false;
        vm.Canvas.SnapToGrid = true;
        vm.Canvas.GridSpacingMm = 50;

        Drag(vm, new Point2D(600, 300), new Point2D(722, 300));

        Assert.Equal(700.0, X(frame, mullion));
    }

    [Fact]
    public void DragWithObjectSnap_ShowsMarker()
    {
        var vm = App(zoom: 1.0);
        var (frame, mullion, _) = SampleDesign(vm);
        vm.Properties.PositionText = "";
        vm.Select(mullion);
        vm.Properties.PositionText = "400";
        vm.Properties.ApplyPositionCommand.Execute(null);

        Drag(vm, new Point2D(400, 300), new Point2D(596, 300), release: false);
        Assert.NotNull(vm.Interaction.Preview.Snap);
        vm.ActiveTool.OnPointerUp(At(vm, new Point2D(596, 300)));

        Assert.Equal(600.0, X(frame, mullion));
    }

    // ── Selection ───────────────────────────────────────────────────

    [Fact]
    public void Click_SingleSelection_ReplacesPrevious()
    {
        var vm = App();
        var (_, mullion, transom) = SampleDesign(vm);
        Click(vm, new Point2D(600, 300));
        Click(vm, new Point2D(300, 750));
        Assert.Equal(new[] { transom }, vm.SelectedIds);
        Assert.Equal("TRANSOM", vm.Properties.Header);
    }

    [Fact]
    public void ShiftClick_AddsToSelection()
    {
        var vm = App();
        var (_, mullion, transom) = SampleDesign(vm);
        Click(vm, new Point2D(600, 300));
        Click(vm, new Point2D(300, 750), shift: true);

        Assert.True(vm.IsSelected(mullion) && vm.IsSelected(transom));
        Assert.Equal("2 OBJECTS SELECTED", vm.Properties.Header);
    }

    [Fact]
    public void CtrlClick_Toggles()
    {
        var vm = App();
        var (_, mullion, _) = SampleDesign(vm);
        Click(vm, new Point2D(600, 300), ctrl: true);
        Assert.True(vm.IsSelected(mullion));
        Click(vm, new Point2D(600, 300), ctrl: true);
        Assert.False(vm.IsSelected(mullion));
    }

    [Fact]
    public void ClickEmpty_Clears_UnlessModifierHeld()
    {
        var vm = App();
        var (_, mullion, _) = SampleDesign(vm);
        Click(vm, new Point2D(600, 300));
        Click(vm, new Point2D(-400, -400), shift: true);
        Assert.True(vm.IsSelected(mullion));
        Click(vm, new Point2D(-400, -400));
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public void BoxSelection_WindowAndCrossing()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);
        var leftGlass = frame.GlassPanels.Where(g => g.Boundary.Left < 600).Select(g => g.Id).ToHashSet();

        // Start points are > 5 px (10 mm at this zoom) outside the frame, so they hit empty space.
        Drag(vm, new Point2D(-40, -40), new Point2D(610, 1540));                     // left → right: window
        Assert.True(vm.SelectedIds.ToHashSet().SetEquals(leftGlass));
        Assert.Null(vm.Interaction.SelectionBox);

        Drag(vm, new Point2D(610, 1540), new Point2D(-40, -40));                     // right → left: crossing
        Assert.Contains(frame.Id, vm.SelectedIds);
        Assert.Contains(mullion, vm.SelectedIds);
    }

    [Fact]
    public void SelectAll_AndDeleteMultiple_AsOneUndo()
    {
        var vm = App();
        var (frame, mullion, transom) = SampleDesign(vm);

        Assert.True(vm.ActiveTool.OnKey(ViewportKey.SelectAll));
        Assert.Equal(1 + 2 + 4, vm.SelectedIds.Count);   // frame, mullion + transom, 4 glass

        vm.Select(mullion);
        vm.Select(transom, addToSelection: true);
        int historyBefore = vm.CommandHistory.UndoCount;
        Assert.True(vm.ActiveTool.OnKey(ViewportKey.Delete));

        Assert.Single(frame.GlassPanels);
        Assert.Equal(historyBefore + 1, vm.CommandHistory.UndoCount);
        vm.UndoCommand.Execute(null);
        Assert.Equal(4, frame.GlassPanels.Count);
    }

    [Fact]
    public void HitTesting_IsZoomIndependent()
    {
        foreach (double zoom in new[] { 0.05, 0.3, 1.0, 5.0 })
        {
            var vm = App(zoom);
            var (frame, mullion, transom) = SampleDesign(vm);

            Click(vm, new Point2D(600, 300));
            Assert.True(vm.IsSelected(mullion), $"mullion at zoom {zoom}");
            Click(vm, new Point2D(300, 750));
            Assert.True(vm.IsSelected(transom), $"transom at zoom {zoom}");
            Click(vm, new Point2D(300, 300));
            Assert.True(vm.Project.Frames[0].GlassPanels.Any(g => vm.IsSelected(g.Id)), $"glass at zoom {zoom}");
            Click(vm, new Point2D(15, 400));
            Assert.True(vm.IsSelected(frame.Id), $"frame at zoom {zoom}");
        }
    }

    // ── Resize handles ──────────────────────────────────────────────

    [Fact]
    public void ResizeHandle_DragChangesWidth_OneUndo()
    {
        var vm = App();
        vm.CreateFrameCommand.Execute(null);
        var frame = vm.Project.Frames.Single();
        Assert.Same(frame, vm.SingleSelectedFrame);
        int historyBefore = vm.CommandHistory.UndoCount;

        Drag(vm, new Point2D(1200, 750), new Point2D(1400, 750));

        Assert.Equal(1400.0, frame.Width);
        Assert.Equal(historyBefore + 1, vm.CommandHistory.UndoCount);
        vm.UndoCommand.Execute(null);
        Assert.Equal(1200.0, frame.Width);
        vm.RedoCommand.Execute(null);
        Assert.Equal(1400.0, frame.Width);
    }

    [Fact]
    public void ResizeHandle_CannotCrushDivisions()
    {
        var vm = App();
        var (frame, _, _) = SampleDesign(vm);
        vm.Select(frame.Id);

        Drag(vm, new Point2D(1200, 750), new Point2D(300, 750), release: false);
        Assert.True(vm.HintIsError);
        vm.ActiveTool.OnPointerUp(At(vm, new Point2D(300, 750)));

        Assert.Equal(740.0, frame.Width);
        Assert.True(FrameLayout.Compute(frame, vm.Rules).IsValid);
    }

    // ── Other tools ─────────────────────────────────────────────────

    [Fact]
    public void FrameTool_DrawsAFrame()
    {
        var vm = App(zoom: 0.25);
        vm.Mode = InteractionMode.CreateFrame;

        Drag(vm, new Point2D(0, 0), new Point2D(1000, 1200));

        var frame = Assert.Single(vm.Project.Frames);
        Assert.Equal((1000.0, 1200.0), (frame.Width, frame.Height));
        Assert.True(vm.IsSelected(frame.Id));
        Assert.Equal(1, vm.CommandHistory.UndoCount);
    }

    [Fact]
    public void MullionTool_PreviewsOnHover_AddsOnClick()
    {
        var vm = App(zoom: 1.0);
        vm.CreateFrameCommand.Execute(null);
        var frame = vm.Project.Frames.Single();
        vm.ObjectSnapEnabled = false;
        vm.Mode = InteractionMode.AddMullion;

        vm.ActiveTool.OnPointerMove(At(vm, new Point2D(450, 700)));
        Assert.True(vm.Interaction.Preview.ReplacementFrames.ContainsKey(frame.Id));
        Assert.Single(frame.GlassPanels);

        vm.ActiveTool.OnPointerDown(At(vm, new Point2D(450, 700)));
        Assert.Equal(450.0, frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).StartPoint.X);
        Assert.Equal(2, frame.GlassPanels.Count);
    }

    [Fact]
    public void PanTool_MovesTheViewNotTheModel()
    {
        var vm = App(zoom: 0.5);
        vm.CreateFrameCommand.Execute(null);
        vm.Canvas.ZoomLevel = 0.5;
        vm.Canvas.PanX = 0;
        vm.Mode = InteractionMode.Pan;
        int history = vm.CommandHistory.UndoCount;

        var start = new Point2D(100, 100);
        vm.ActiveTool.OnPointerDown(new ViewportPointerEventArgs(start, vm.Canvas.ScreenToWorld(start), false));
        vm.ActiveTool.OnPointerMove(new ViewportPointerEventArgs(new Point2D(200, 100), default, false));
        vm.ActiveTool.OnPointerUp(new ViewportPointerEventArgs(new Point2D(200, 100), default, false));

        Near(-200.0, vm.Canvas.PanX);   // 100 px at 0.5 px/mm
        Assert.Equal(history, vm.CommandHistory.UndoCount);
        Assert.Equal(0.0, vm.Project.Frames[0].X);
    }

    [Fact]
    public void EscapeInCreationTool_ReturnsToSelect()
    {
        var vm = App();
        vm.Mode = InteractionMode.AddTransom;
        Assert.True(vm.ActiveTool.OnKey(ViewportKey.Escape));
        Assert.Equal(InteractionMode.Select, vm.Mode);
    }

    [Fact]
    public void SwitchingTool_CancelsTheDragInProgress()
    {
        var vm = App();
        var (frame, mullion, _) = SampleDesign(vm);
        Drag(vm, new Point2D(600, 300), new Point2D(800, 300), release: false);

        vm.Mode = InteractionMode.Pan;

        Assert.True(vm.Interaction.Preview.IsEmpty);
        Assert.Equal(600.0, X(frame, mullion));
    }

    [Fact]
    public void UndoDuringDrag_CancelsTheDragFirst()
    {
        var vm = App();
        var (frame, mullion, transom) = SampleDesign(vm);
        Drag(vm, new Point2D(600, 300), new Point2D(800, 300), release: false);

        vm.UndoCommand.Execute(null);   // undoes the transom position edit, not a half-finished drag

        Assert.True(vm.Interaction.Preview.IsEmpty);
        Assert.False(vm.ActiveTool.IsCapturing);
        Assert.Equal(600.0, X(frame, mullion));
    }
}
