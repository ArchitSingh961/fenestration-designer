using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Interaction;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Core.Snapping;
using Xunit;
using static Mark.Tests.GeometryAssert;

namespace Mark.Tests.Interaction;

/// <summary>WPF-free interaction layer: selection, box selection, handles, clamping and the drag operations.</summary>
public class CoreInteractionTests
{
    private static readonly DesignRules Rules = new();
    private const double Tol = 5.0;

    private static SnapEngine Engine(SnapSettings? settings = null) => new(settings ?? new SnapSettings(), Rules);

    private static (Project Project, Frame Frame) OneFrame(params double[] mullions)
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        foreach (var x in mullions)
            FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, x, Rules);
        var project = new Project();
        project.Frames.Add(frame);
        return (project, frame);
    }

    private static Profile Mullion(Frame frame, double x) => frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion && p.StartPoint.X == x);

    private static double MullionX(Frame frame, Guid id) => frame.Profiles.Single(p => p.Id == id).StartPoint.X;

    // ── Selection service ───────────────────────────────────────────

    [Fact]
    public void Selection_SelectAddRemoveToggleClear()
    {
        var selection = new SelectionService();
        int changes = 0;
        selection.Changed += () => changes++;
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        selection.Select(a);
        Assert.Equal(new[] { a }, selection.SelectedIds);
        selection.Add(b);
        Assert.Equal(2, selection.Count);
        selection.Remove(a);
        Assert.False(selection.Contains(a));
        selection.Toggle(a);
        Assert.True(selection.Contains(a));
        selection.Toggle(a);
        Assert.False(selection.Contains(a));
        selection.Clear();
        Assert.Equal(0, selection.Count);

        Assert.Equal(6, changes);
    }

    [Fact]
    public void Selection_NoEventWhenNothingChanges()
    {
        var selection = new SelectionService();
        var a = Guid.NewGuid();
        selection.Select(a);
        int changes = 0;
        selection.Changed += () => changes++;

        selection.Select(a);
        selection.Add(a);
        selection.Remove(Guid.NewGuid());
        selection.SelectMany(new[] { a });

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Selection_PruneDropsMissingObjects()
    {
        var selection = new SelectionService();
        Guid keep = Guid.NewGuid(), gone = Guid.NewGuid();
        selection.SelectMany(new[] { keep, gone });

        Assert.Equal(1, selection.Prune(id => id == keep));
        Assert.Equal(new[] { keep }, selection.SelectedIds);
    }

    [Fact]
    public void SelectionQuery_WindowVsCrossing()
    {
        var (project, frame) = OneFrame(600);
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 750, Rules);
        var box = Rectangle2D.FromCorners(new Point2D(-10, -10), new Point2D(610, 1510));
        var leftGlass = frame.GlassPanels.Where(g => g.Boundary.Left < 600).Select(g => g.Id).ToHashSet();

        var window = SelectionQuery.InRectangle(project, box, crossing: false).ToHashSet();
        Assert.True(window.SetEquals(leftGlass));   // only what is completely inside

        var crossing = SelectionQuery.InRectangle(project, box, crossing: true).ToHashSet();
        Assert.Contains(frame.Id, crossing);
        Assert.Contains(Mullion(frame, 600).Id, crossing);
        Assert.Contains(frame.Profiles.Single(p => p.ProfileType == ProfileType.Transom).Id, crossing);
        Assert.True(leftGlass.IsSubsetOf(crossing));
        Assert.Equal(5, crossing.Count);   // right-hand glass (starting at 630) isn't touched
    }

    [Fact]
    public void SelectionQuery_All()
    {
        var (project, frame) = OneFrame(600);
        Assert.Equal(1 + 1 + 2, SelectionQuery.All(project).Count);   // frame, mullion, 2 glass
    }

    // ── Handles ─────────────────────────────────────────────────────

    [Fact]
    public void Handles_HitTestInWorldMm()
    {
        var (_, frame) = OneFrame();
        Assert.Equal(FrameHandle.Right, FrameHandles.HitTest(frame, new Point2D(1203, 752), 5));
        Assert.Equal(FrameHandle.Bottom, FrameHandles.HitTest(frame, new Point2D(600, 1498), 5));
        Assert.Equal(FrameHandle.BottomRight, FrameHandles.HitTest(frame, new Point2D(1201, 1501), 5));
        Assert.Null(FrameHandles.HitTest(frame, new Point2D(1210, 752), 5));
        Assert.Equal(FrameHandle.Right, FrameHandles.HitTest(frame, new Point2D(1210, 752), 14));
    }

    // ── Drag clamp ──────────────────────────────────────────────────

    [Fact]
    public void DragClamp_FindsFurthestValidWholeMm()
    {
        var clamped = DragClamp.Clamp(Vector2D.Zero, new Vector2D(2000, 0), v => v.X <= 460.4, 1);
        Assert.Equal(new Vector2D(460, 0), clamped);
    }

    // ── Move ────────────────────────────────────────────────────────

    [Fact]
    public void Move_ValidDrag_PreviewsWithoutTouchingTheModel_ThenOneCommand()
    {
        var (project, frame) = OneFrame(600);
        var id = Mullion(frame, 600).Id;
        string before = ProjectSerializer.Serialize(project);

        var op = new MoveElementsOperation(project, new[] { id }, id, new Point2D(600, 300), Rules, Engine());
        var preview = op.Update(new Point2D(700, 300), Tol);

        Assert.True(preview.IsValid);
        Assert.Equal("Mullion at 700 mm", preview.Message);
        Assert.Equal(700.0, MullionX(preview.ReplacementFrames[frame.Id], id));
        Assert.Equal(before, ProjectSerializer.Serialize(project));   // committed model untouched

        var history = new CommandHistory();
        history.Execute(op.CreateCommand()!);
        Assert.Equal(700.0, MullionX(frame, id));
        history.Undo();
        Assert.Equal(before, ProjectSerializer.Serialize(project));
    }

    [Fact]
    public void Move_WithSnapping()
    {
        var (project, frame) = OneFrame(400);
        var id = Mullion(frame, 400).Id;
        var op = new MoveElementsOperation(project, new[] { id }, id, new Point2D(400, 300), Rules, Engine());

        var preview = op.Update(new Point2D(597, 300), Tol);

        Assert.Equal(600.0, MullionX(preview.ReplacementFrames[frame.Id], id));
        Assert.NotNull(preview.Snap);
        Assert.Equal(Core.Interfaces.SnapType.Midpoint, preview.Snap!.Value.Type);
    }

    [Fact]
    public void Move_InvalidDrag_IsNeverCommitted_PreviewStopsAtLimit()
    {
        var (project, frame) = OneFrame(600);
        var id = Mullion(frame, 600).Id;
        var op = new MoveElementsOperation(project, new[] { id }, id, new Point2D(600, 300), Rules, Engine());

        var preview = op.Update(new Point2D(1150, 300), Tol);

        Assert.False(preview.IsValid);
        Assert.Contains("minimum 50 mm", preview.Message);
        Assert.Single(preview.Ghosts);                                   // the invalid candidate, shown as a warning
        Assert.Equal(1060.0, MullionX(preview.ReplacementFrames[frame.Id], id));   // last valid position
        Assert.Equal(600.0, MullionX(frame, id));                        // model untouched

        op.CreateCommand()!.Execute();
        Assert.Equal(1060.0, MullionX(frame, id));
        Assert.True(FrameLayout.Compute(frame, Rules).IsValid);
    }

    [Fact]
    public void Move_TwoMullionsInOneFrame_OneCommand_OneUndo()
    {
        var (project, frame) = OneFrame(400, 800);
        Guid a = Mullion(frame, 400).Id, b = Mullion(frame, 800).Id;
        var op = new MoveElementsOperation(project, new[] { a, b }, a, new Point2D(400, 300), Rules, Engine());

        op.Update(new Point2D(450, 300), Tol);
        var command = op.CreateCommand();
        Assert.IsType<MoveDivisionsCommand>(command);

        var history = new CommandHistory();
        history.Execute(command!);
        Assert.Equal((450.0, 850.0), (MullionX(frame, a), MullionX(frame, b)));
        history.Undo();
        Assert.Equal((400.0, 800.0), (MullionX(frame, a), MullionX(frame, b)));
    }

    [Fact]
    public void Move_AcrossFrames_IsOneCompositeCommand()
    {
        var project = new Project();
        var left = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        var right = FrameEditor.CreateFrame(2000, 0, 1200, 1500, Rules);
        project.Frames.Add(left);
        project.Frames.Add(right);
        var a = FrameEditor.AddDivision(left, MemberAxis.Vertical, null, 400, Rules);
        var b = FrameEditor.AddDivision(right, MemberAxis.Vertical, null, 400, Rules);

        var op = new MoveElementsOperation(project, new[] { a, b }, a, new Point2D(400, 300), Rules, Engine());
        op.Update(new Point2D(450, 300), Tol);
        var command = Assert.IsType<CompositeCommand>(op.CreateCommand());
        Assert.Equal(2, command.Commands.Count);

        var history = new CommandHistory();
        history.Execute(command);
        Assert.Equal((450.0, 450.0), (MullionX(left, a), MullionX(right, b)));
        history.Undo();
        Assert.Equal((400.0, 400.0), (MullionX(left, a), MullionX(right, b)));
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void Move_WholeFrame_CarriesItsDivisions()
    {
        var (project, frame) = OneFrame(600);
        var mullion = Mullion(frame, 600).Id;
        var op = new MoveElementsOperation(project, new[] { frame.Id, mullion }, frame.Id, new Point2D(100, 100), Rules, Engine());

        op.Update(new Point2D(350, 180), Tol);
        var command = Assert.IsType<MoveFrameCommand>(op.CreateCommand());
        command.Execute();

        Assert.Equal((250.0, 80.0), (frame.X, frame.Y));
        Assert.Equal(600.0, MullionX(frame, mullion));   // frame-relative position unchanged
        command.Undo();
        Assert.Equal((0.0, 0.0), (frame.X, frame.Y));
    }

    [Fact]
    public void Move_GlassOnly_IsEmpty()
    {
        var (project, frame) = OneFrame(600);
        var glass = frame.GlassPanels[0].Id;
        var op = new MoveElementsOperation(project, new[] { glass }, glass, new Point2D(300, 300), Rules, Engine());
        Assert.True(op.IsEmpty);
        Assert.Null(op.CreateCommand());
    }

    [Fact]
    public void Move_NoMovement_NoCommand()
    {
        var (project, frame) = OneFrame(600);
        var id = Mullion(frame, 600).Id;
        var op = new MoveElementsOperation(project, new[] { id }, id, new Point2D(600, 300), Rules, Engine());
        op.Update(new Point2D(600.2, 300), Tol);
        Assert.Null(op.CreateCommand());
    }

    // ── Resize ──────────────────────────────────────────────────────

    [Fact]
    public void Resize_RightHandle_ValidPreviewThenCommand()
    {
        var (project, frame) = OneFrame();
        var op = new ResizeFrameOperation(project, frame, FrameHandle.Right, new Point2D(1200, 750), Rules, Engine());

        var preview = op.Update(new Point2D(1400, 760), Tol);

        Assert.True(preview.IsValid);
        Assert.Equal(1400.0, preview.ReplacementFrames[frame.Id].Width);
        Assert.Equal(1500.0, preview.ReplacementFrames[frame.Id].Height);   // the right handle only changes width
        Assert.Equal(1200.0, frame.Width);

        var history = new CommandHistory();
        history.Execute(op.CreateCommand()!);
        Assert.Equal(1400.0, frame.Width);
        history.Undo();
        Assert.Equal(1200.0, frame.Width);
    }

    [Fact]
    public void Resize_PastADivision_IsRejected_AndStopsAtTheLimit()
    {
        var (project, frame) = OneFrame(600);
        var op = new ResizeFrameOperation(project, frame, FrameHandle.Right, new Point2D(1200, 750), Rules, Engine());

        var preview = op.Update(new Point2D(500, 750), Tol);

        Assert.False(preview.IsValid);
        Assert.StartsWith("Cannot resize the frame", preview.Message);
        Assert.Single(preview.Ghosts);
        // mullion face 630 + 50 mm glass + 60 mm frame = 740 mm minimum width.
        Assert.Equal(740.0, preview.ReplacementFrames[frame.Id].Width);
        Assert.Equal(600.0, MullionX(frame, Mullion(frame, 600).Id));   // divisions are never moved to make room
    }

    [Fact]
    public void Resize_CornerHandle_ChangesBoth()
    {
        var (project, frame) = OneFrame();
        var op = new ResizeFrameOperation(project, frame, FrameHandle.BottomRight, new Point2D(1200, 1500), Rules, Engine());
        op.Update(new Point2D(1300, 1700), Tol);
        op.CreateCommand()!.Execute();
        Assert.Equal((1300.0, 1700.0), (frame.Width, frame.Height));
    }

    // ── Create frame / add division ─────────────────────────────────

    [Fact]
    public void CreateFrame_ByDragging()
    {
        var project = new Project();
        var op = new CreateFrameOperation(project, new Point2D(0.3, -0.2), Tol, Rules, Engine());

        var preview = op.Update(new Point2D(1200.4, 1499.6), Tol);
        Assert.True(preview.IsValid);
        Assert.Single(preview.Ghosts);
        Assert.Equal("New frame 1200 × 1500 mm", preview.Message);
        Assert.Empty(project.Frames);

        op.CreateCommand()!.Execute();
        var frame = Assert.Single(project.Frames);
        Assert.Equal((0.0, 0.0, 1200.0, 1500.0), (frame.X, frame.Y, frame.Width, frame.Height));
        Assert.Single(frame.GlassPanels);
    }

    [Fact]
    public void CreateFrame_TooSmall_IsInvalid_AndCreatesNothing()
    {
        var project = new Project();
        var op = new CreateFrameOperation(project, new Point2D(0, 0), Tol, Rules, Engine());
        var preview = op.Update(new Point2D(100, 100), Tol);
        Assert.False(preview.IsValid);
        Assert.Null(op.CreateCommand());
    }

    [Fact]
    public void AddDivision_HoverPreview_ThenClickCommits()
    {
        var (project, frame) = OneFrame();
        var op = new AddDivisionOperation(project, MemberAxis.Vertical, Rules, Engine());

        var preview = op.Update(new Point2D(450.3, 700), Tol, wholeFrame: false);
        Assert.True(preview.IsValid);
        Assert.Equal("Mullion at 450 mm", preview.Message);
        Assert.Equal(2, preview.ReplacementFrames[frame.Id].GlassPanels.Count);
        Assert.Single(frame.GlassPanels);

        op.CreateCommand()!.Execute();
        Assert.Equal(450.0, frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion).StartPoint.X);
    }

    [Fact]
    public void AddDivision_TooCloseToFrame_IsInvalid()
    {
        var (project, _) = OneFrame();
        var op = new AddDivisionOperation(project, MemberAxis.Vertical, Rules, Engine(new SnapSettings { ObjectSnapEnabled = false }));
        var preview = op.Update(new Point2D(100, 700), Tol, wholeFrame: false);
        Assert.False(preview.IsValid);
        Assert.Single(preview.Ghosts);
        Assert.Null(op.CreateCommand());
    }

    [Fact]
    public void AddDivision_OutsideAnyFrame_HasNoCandidate()
    {
        var (project, _) = OneFrame();
        var op = new AddDivisionOperation(project, MemberAxis.Horizontal, Rules, Engine());
        Assert.False(op.Update(new Point2D(5000, 700), Tol, false).IsValid);
        Assert.Null(op.CreateCommand());
    }

    [Fact]
    public void AddDivision_Shift_SpansTheWholeFrame()
    {
        var (project, frame) = OneFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 750, Rules);
        var op = new AddDivisionOperation(project, MemberAxis.Vertical, Rules, Engine(new SnapSettings { ObjectSnapEnabled = false }));

        op.Update(new Point2D(450, 300), Tol, wholeFrame: true);
        op.CreateCommand()!.Execute();

        var mullion = frame.Profiles.Single(p => p.ProfileType == ProfileType.Mullion);
        Assert.Equal((30.0, 1470.0), (mullion.StartPoint.Y, mullion.EndPoint.Y));   // crosses the transom
        Assert.Equal(4, frame.GlassPanels.Count);
    }
}
