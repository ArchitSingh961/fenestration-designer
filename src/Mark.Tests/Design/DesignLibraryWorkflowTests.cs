using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;
using Mark.Designer.Interaction;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Design;

/// <summary>
/// Milestone 9 through the view model, as the user drives it: the design library (click and drag-and-drop), the
/// opening and design-details editors in the properties panel, and the Inside / Outside view.
/// </summary>
public class DesignLibraryWorkflowTests
{
    private static DesignTemplate Template(string id) => DesignTemplates.Find(id)!;

    private static (MainViewModel Vm, FakeDialogs Dialogs) NewDesigner()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel { Dialogs = dialogs };
        return (vm, dialogs);
    }

    private static Frame AddFrame(MainViewModel vm, double width = 1500, double height = 1500)
    {
        vm.NewFrameWidthText = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.NewFrameHeightText = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.CreateFrame();
        return vm.Project.Frames[^1];
    }

    private static IViewportDropTarget Drop(MainViewModel vm) => vm;

    // ── Library panel ───────────────────────────────────────────────

    [Fact]
    public void TheLibrary_StartsOnTheFrameTools_AndListsEveryCategory()
    {
        var (vm, _) = NewDesigner();

        Assert.True(vm.DesignLibrary.IsFramePanel);
        Assert.Empty(vm.DesignLibrary.Sections);
        Assert.Equal(new[] { "Frame" }.Concat(DesignTemplates.Categories), vm.DesignLibrary.Categories.Select(c => c.Name));

        vm.DesignLibrary.SelectedCategory = DesignTemplates.Openable;
        Assert.False(vm.DesignLibrary.IsFramePanel);
        Assert.Contains(vm.DesignLibrary.Sections, s => s.Title == "Tilt & turn designs");
        Assert.All(vm.DesignLibrary.Sections.SelectMany(s => s.Items), item => Assert.NotNull(item.Thumbnail));
    }

    [Fact]
    public void ClickingADesign_WithNoFrame_CreatesANewFrameWithIt_AsOneUndoStep()
    {
        var (vm, _) = NewDesigner();
        vm.DesignLibrary.SelectedCategory = DesignTemplates.Sliding;
        var item = vm.DesignLibrary.Sections.SelectMany(s => s.Items).First(i => i.Template.Id == "sld-2");

        item.ApplyCommand.Execute(null);

        var frame = Assert.Single(vm.Project.Frames);
        Assert.Equal("W1", frame.Design.Reference);
        Assert.Equal(new[] { OpeningType.SlidingRight, OpeningType.SlidingLeft }, frame.GlassPanels.Select(g => g.Opening));
        Assert.True(vm.IsSelected(frame.Id));
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Project.Frames);
    }

    [Fact]
    public void ClickingADesign_AppliesItToTheSelectedOpeningOnly()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm, 2000, 1500);
        vm.ApplyDesign(Template("div-v2"));
        var right = frame.GlassPanels.OrderBy(g => g.Boundary.Left).Last();
        vm.Select(right.Id);

        Assert.Null(vm.ApplyDesign(Template("cas-right")));

        var panels = frame.GlassPanels.OrderBy(g => g.Boundary.Left).ToList();
        Assert.Equal(OpeningType.Fixed, panels[0].Opening);
        Assert.Equal(OpeningType.SideHungRight, panels[1].Opening);
    }

    [Fact]
    public void ReplacingAWholeFramesDesign_AsksFirst_AndNoMeansNoChange()
    {
        var (vm, dialogs) = NewDesigner();
        var frame = AddFrame(vm);
        vm.ApplyDesign(Template("cas-french"));
        int panels = frame.GlassPanels.Count;
        vm.Select(frame.Id);
        dialogs.ConfirmAnswer = false;

        Assert.Null(vm.ApplyDesign(Template("sld-3")));

        Assert.Contains("W1", Assert.Single(dialogs.Confirms));
        Assert.Equal(panels, frame.GlassPanels.Count);

        dialogs.ConfirmAnswer = true;
        vm.ApplyDesign(Template("sld-3"));
        Assert.Equal(3, frame.GlassPanels.Count);
    }

    [Fact]
    public void ADesignOnAPlainFrame_DoesNotAsk()
    {
        var (vm, dialogs) = NewDesigner();
        AddFrame(vm);

        vm.ApplyDesign(Template("tt-left"));

        Assert.Empty(dialogs.Confirms);
        Assert.Equal(OpeningType.TiltTurnLeft, vm.Project.Frames[0].GlassPanels[0].Opening);
    }

    [Fact]
    public void ADesignThatDoesNotFit_ShowsWhy_AndChangesNothing()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm, 800, 1200);
        vm.DesignLibrary.SelectedCategory = DesignTemplates.Sliding;
        var item = vm.DesignLibrary.Sections.SelectMany(s => s.Items).First(i => i.Template.Id == "sld-6");

        item.ApplyCommand.Execute(null);

        Assert.True(vm.DesignLibrary.HasMessage);
        Assert.StartsWith("Cannot apply", vm.DesignLibrary.Message);
        Assert.Single(frame.GlassPanels);
    }

    // ── Drag and drop ───────────────────────────────────────────────

    [Fact]
    public void DraggingOverAnOpening_HighlightsIt_AndDroppingAppliesTheDesignThere()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm, 2000, 1500);
        vm.ApplyDesign(Template("div-v2"));
        var left = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        var point = new Point2D(frame.X + left.Boundary.Center.X, frame.Y + left.Boundary.Center.Y);

        Assert.True(Drop(vm).DragOver(point, "twin-left"));
        Assert.Equal(left.Boundary.Offset(frame.X, frame.Y), vm.Interaction.DropTarget);

        Assert.True(Drop(vm).Drop(point, "twin-left"));
        Assert.Null(vm.Interaction.DropTarget);
        var dropped = frame.GlassPanels.Single(g => g.Id == left.Id);
        Assert.Equal(OpeningType.SideHungLeft, dropped.Opening);
        Assert.True(dropped.HasMesh);
        Assert.True(vm.IsSelected(left.Id));
    }

    [Fact]
    public void DroppingOnEmptySpace_CreatesANewFrameThere()
    {
        var (vm, _) = NewDesigner();
        AddFrame(vm);

        Assert.True(Drop(vm).Drop(new Point2D(3004, 498), "cas-left"));

        var created = vm.Project.Frames[^1];
        Assert.Equal(2, vm.Project.Frames.Count);
        Assert.Equal((3000.0, 500.0), (created.X, created.Y));
        Assert.Equal(OpeningType.SideHungLeft, created.GlassPanels[0].Opening);
        Assert.Equal("W2", created.Design.Reference);
    }

    [Fact]
    public void DragLeave_ClearsTheHighlight_AndUnknownDesignsAreRefused()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm);
        Drop(vm).DragOver(frame.Center, "cas-left");
        Drop(vm).DragLeave();
        Assert.Null(vm.Interaction.DropTarget);

        Assert.False(Drop(vm).DragOver(frame.Center, "no-such-design"));
        Assert.False(Drop(vm).Drop(frame.Center, "no-such-design"));
    }

    // ── Properties panel editors ────────────────────────────────────

    [Fact]
    public void SelectingAnOpening_OffersItsType_AndChangingItIsUndoable()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm);
        vm.Select(frame.GlassPanels[0].Id);
        var editor = vm.Properties.OpeningEditor!;
        Assert.Equal(OpeningType.Fixed, editor.SelectedOption!.Type);

        editor.SelectedOption = OpeningEditorViewModel.AllOptions.First(o => o.Type == OpeningType.TopHung);
        Assert.Equal(OpeningType.TopHung, frame.GlassPanels[0].Opening);

        vm.Properties.OpeningEditor!.HasMesh = true;
        Assert.True(frame.GlassPanels[0].HasMesh);
        Assert.Equal(OpeningType.TopHung, frame.GlassPanels[0].Opening);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(OpeningType.Fixed, frame.GlassPanels[0].Opening);
        Assert.False(frame.GlassPanels[0].HasMesh);
    }

    [Fact]
    public void ARejectedOpeningType_ShowsWhy_AndTheEditorKeepsTheModelValue()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm, 400, 1500);
        vm.ApplyDesign(Template("div-v2"));
        vm.Select(frame.GlassPanels[0].Id);
        var editor = vm.Properties.OpeningEditor!;

        editor.SelectedOption = OpeningEditorViewModel.AllOptions.First(o => o.Type == OpeningType.SideHungLeft);

        Assert.True(editor.HasError);
        Assert.Equal(OpeningType.Fixed, editor.SelectedOption!.Type);
        Assert.Equal(OpeningType.Fixed, frame.GlassPanels[0].Opening);
    }

    [Fact]
    public void SeveralOpenings_ShowMixed_AndMeshChangesKeepEachType()
    {
        var (vm, _) = NewDesigner();
        AddFrame(vm, 2000, 1500);
        vm.ApplyDesign(Template("cas-fixed-left"));
        vm.SelectAll();
        var editor = vm.Properties.OpeningEditor!;
        Assert.Null(editor.SelectedOption);

        editor.HasMesh = true;

        var panels = vm.Project.Frames[0].GlassPanels.OrderBy(g => g.Boundary.Left).ToList();
        Assert.All(panels, p => Assert.True(p.HasMesh));
        Assert.Equal(OpeningType.Fixed, panels[0].Opening);
        Assert.Equal(OpeningType.SideHungRight, panels[1].Opening);
    }

    [Fact]
    public void TheDesignDetailsEditor_StoresReferenceQuantityAndFloorDistance()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm);
        vm.Select(frame.Id);
        var editor = vm.Properties.DesignEditor!;
        Assert.Equal("W1", editor.Reference);

        editor.Reference = "W5";
        editor.QuantityText = "3";
        editor.Location = "Kitchen";
        editor.FloorDistanceText = "900";
        editor.ApplyCommand.Execute(null);

        Assert.Equal(("W5", 3, "Kitchen", (double?)900), (frame.Design.Reference, frame.Design.Quantity, frame.Design.Location, frame.Design.FloorDistanceMm));
        Assert.Equal("W5", vm.Properties.DesignEditor!.Reference);
    }

    [Fact]
    public void TheDesignDetailsEditor_RejectsABadQuantity()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm);
        vm.Select(frame.Id);
        var editor = vm.Properties.DesignEditor!;

        editor.QuantityText = "two";
        editor.ApplyCommand.Execute(null);
        Assert.True(editor.HasError);

        editor.QuantityText = "0";
        editor.ApplyCommand.Execute(null);
        Assert.True(editor.HasError);
        Assert.Equal(1, frame.Design.Quantity);
    }

    // ── Inside / Outside ────────────────────────────────────────────

    [Fact]
    public void TheOutsideView_IsReadOnly_AndOnlyPans()
    {
        var (vm, _) = NewDesigner();
        var frame = AddFrame(vm);
        var selectTool = vm.ActiveTool;

        vm.IsOutsideView = true;

        Assert.False(vm.IsInsideView);
        Assert.NotSame(selectTool, vm.ActiveTool);
        Assert.Same(vm.ToolFor(InteractionMode.Pan), vm.ActiveTool);
        Assert.Contains("Outside", vm.ModeHint);
        Assert.NotNull(vm.ApplyDesign(Template("cas-left")));
        Assert.False(Drop(vm).DragOver(frame.Center, "cas-left"));
        Assert.Equal(OpeningType.Fixed, frame.GlassPanels[0].Opening);

        vm.IsInsideView = true;
        Assert.Same(selectTool, vm.ActiveTool);
    }
}
