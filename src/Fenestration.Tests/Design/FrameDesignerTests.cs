using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Core.Serialization;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Design;

/// <summary>
/// Frame designer behaviour. Default rules: 60 mm frame, mullion and transom profiles; 50 mm minimum glass.
/// So a 1200 × 1500 frame has centrelines at 30 / 1170 (X) and 30 / 1470 (Y), and its opening is 1080 × 1380.
/// </summary>
public class FrameDesignerTests
{
    private static readonly DesignRules Rules = new();

    private static Frame NewFrame(double width = 1200, double height = 1500) => FrameEditor.CreateFrame(0, 0, width, height, Rules);

    private static Profile Division(Frame frame, Guid id) => frame.Profiles.Single(p => p.Id == id);

    private static List<Rectangle2D> Glass(Frame frame)
        => frame.GlassPanels.Select(g => g.Boundary).OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();

    /// <summary>The sample design from the milestone spec: 1200 × 1500, mullion at 600, transom at 750.</summary>
    private static (Frame Frame, Guid Mullion, Guid Transom) SampleDesign()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 750, Rules);
        return (frame, mullion, transom);
    }

    // ── Frame ───────────────────────────────────────────────────────

    [Fact]
    public void CreateFrame_BuildsFourOuterProfilesAndOneGlass()
    {
        var frame = NewFrame();

        Assert.Equal(1200.0, frame.Width);
        Assert.Equal(1500.0, frame.Height);
        var outer = FrameMembers.Find(frame);
        Assert.NotNull(outer);
        Assert.All(frame.Profiles, p => Assert.Equal(ProfileType.Frame, p.ProfileType));
        Near(new Rectangle2D(30, 30, 1140, 1440), outer!.CenterlineLoop);

        var glass = Assert.Single(frame.GlassPanels);
        Near(new Rectangle2D(60, 60, 1080, 1380), glass.Boundary);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(-500, 1500)]
    [InlineData(1200, 0)]
    [InlineData(1200, -1)]
    [InlineData(double.NaN, 1500)]
    [InlineData(1200, 40_000)]
    [InlineData(150, 1500)]   // < 2 × 60 frame + 50 glass
    public void CreateFrame_RejectsInvalidSize(double width, double height)
    {
        Assert.Throws<DesignValidationException>(() => NewFrame(width, height));
    }

    [Fact]
    public void CreateFrame_ErrorMessageIsClear()
    {
        var ex = Assert.Throws<DesignValidationException>(() => NewFrame(-500, 1500));
        Assert.Contains("Width", ex.Message);
    }

    [Fact]
    public void ResizeFrame_MovesRightAndBottomMembers_AndGlass()
    {
        var frame = NewFrame();
        FrameEditor.Resize(frame, 1400, 1600, Rules);

        Assert.Equal(1400.0, frame.Width);
        Assert.Equal(1600.0, frame.Height);
        Near(new Rectangle2D(30, 30, 1340, 1540), FrameMembers.Find(frame)!.CenterlineLoop);
        Near(new Rectangle2D(60, 60, 1280, 1480), Assert.Single(frame.GlassPanels).Boundary);
    }

    [Fact]
    public void ResizeFrame_InvalidSize_LeavesFrameUnchanged()
    {
        var frame = NewFrame();
        Assert.Throws<DesignValidationException>(() => FrameEditor.Resize(frame, 0, 1500, Rules));
        Assert.Equal(1200.0, frame.Width);
    }

    // ── Sample design (spec §28) ────────────────────────────────────

    [Fact]
    public void SampleDesign_HasFourGlassRegions()
    {
        var (frame, mullion, transom) = SampleDesign();

        Assert.Equal(1200.0, frame.Width);
        Assert.Equal(1500.0, frame.Height);
        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));
        Assert.Equal(750.0, Members.DivisionPosition(Division(frame, transom)));

        var glass = Glass(frame);
        Assert.Equal(4, glass.Count);
        // X: 60..570 | 630..1140   Y: 60..720 | 780..1440
        Near(new Rectangle2D(60, 60, 510, 660), glass[0]);
        Near(new Rectangle2D(630, 60, 510, 660), glass[1]);
        Near(new Rectangle2D(60, 780, 510, 660), glass[2]);
        Near(new Rectangle2D(630, 780, 510, 660), glass[3]);
    }

    [Fact]
    public void AddDivision_DefaultPositionsAreCentred()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, null, Rules);
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, null, Rules);

        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));
        Assert.Equal(750.0, Members.DivisionPosition(Division(frame, transom)));
        Assert.Equal(4, frame.GlassPanels.Count);
    }

    [Fact]
    public void AddMullion_SpansTheFullOpening()
    {
        var frame = NewFrame();
        var id = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);

        var mullion = Division(frame, id);
        Assert.Equal(ProfileType.Mullion, mullion.ProfileType);
        Assert.Equal(new Point2D(600, 30), mullion.StartPoint);
        Assert.Equal(new Point2D(600, 1470), mullion.EndPoint);
        Assert.Equal(Rules.MullionThicknessMm, mullion.Thickness);
        Assert.Equal(2, frame.GlassPanels.Count);
    }

    // ── Moving divisions updates glass ──────────────────────────────

    [Fact]
    public void MoveMullion_UpdatesGlassWidths()
    {
        var (frame, mullion, _) = SampleDesign();
        FrameEditor.MoveDivision(frame, mullion, 700, Rules);

        Assert.Equal(700.0, Members.DivisionPosition(Division(frame, mullion)));
        var glass = Glass(frame);
        Near(610.0, glass[0].Width);   // 60..670
        Near(410.0, glass[1].Width);   // 730..1140
        Near(660.0, glass[0].Height);
    }

    [Fact]
    public void MoveTransom_UpdatesGlassHeights()
    {
        var (frame, _, transom) = SampleDesign();
        FrameEditor.MoveDivision(frame, transom, 500, Rules);

        var glass = Glass(frame);
        Near(410.0, glass[0].Height);   // 60..470
        Near(910.0, glass[2].Height);   // 530..1440
    }

    [Fact]
    public void ResizeFrame_UpdatesGlass_DivisionsKeepPositions()
    {
        var (frame, mullion, transom) = SampleDesign();
        FrameEditor.Resize(frame, 1400, 1600, Rules);

        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));
        Assert.Equal(750.0, Members.DivisionPosition(Division(frame, transom)));
        // Full-width transom and full-height mullion follow the moved right/bottom members.
        Assert.Equal(1570.0, Division(frame, mullion).EndPoint.Y);
        Assert.Equal(1370.0, Division(frame, transom).EndPoint.X);

        var glass = Glass(frame);
        Near(710.0, glass[1].Width);    // 630..1340
        Near(760.0, glass[3].Height);   // 780..1540
    }

    [Fact]
    public void GlassIds_ArePreservedWhenDivisionsMove()
    {
        var (frame, mullion, _) = SampleDesign();
        var idsBefore = Glass(frame).Select(r => frame.GlassPanels.Single(g => g.Boundary == r).Id).ToList();

        FrameEditor.MoveDivision(frame, mullion, 800, Rules);

        var idsAfter = Glass(frame).Select(r => frame.GlassPanels.Single(g => g.Boundary == r).Id).ToList();
        Assert.Equal(idsBefore, idsAfter);
    }

    // ── Edge cases (spec §32) ───────────────────────────────────────

    [Theory]
    [InlineData(-100)]
    [InlineData(1300)]
    [InlineData(1200)]
    public void Mullion_OutsideFrame_IsRejected(double x)
    {
        var frame = NewFrame();
        Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, x, Rules));
        Assert.Single(frame.GlassPanels);
        Assert.Equal(4, frame.Profiles.Count);
    }

    [Fact]
    public void Transom_OutsideFrame_IsRejected()
    {
        var frame = NewFrame();
        Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 1600, Rules));
    }

    [Theory]
    [InlineData(30)]    // on the frame centreline
    [InlineData(100)]   // leaves 10 mm of glass
    [InlineData(1140)]
    public void Mullion_AtFrameEdge_IsRejected(double x)
    {
        var frame = NewFrame();
        var ex = Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, x, Rules));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Transom_AtFrameEdge_IsRejected()
    {
        var frame = NewFrame();
        Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 1460, Rules));
    }

    [Fact]
    public void TwoDivisionsAtSamePosition_AreRejected()
    {
        var (frame, _, _) = SampleDesign();
        var ex = Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules));
        Assert.Contains("same position", ex.Message);
    }

    [Fact]
    public void ExtremelySmallGlass_IsRejected()
    {
        var (frame, mullion, _) = SampleDesign();
        // A second mullion 90 mm away leaves 90 − 60 = 30 mm of glass (< 50).
        var ex = Assert.Throws<DesignValidationException>(() => FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 690, Rules));
        Assert.Contains("minimum 50 mm", ex.Message);
        // Moving to 680 (20 mm glass) is rejected too, and the mullion stays put.
        var other = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 900, Rules);
        Assert.Throws<DesignValidationException>(() => FrameEditor.MoveDivision(frame, other, 680, Rules));
        Assert.Equal(900.0, Members.DivisionPosition(Division(frame, other)));
        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));
    }

    [Fact]
    public void ResizeBelowDivision_IsRejected_AndNothingChanges()
    {
        var (frame, _, _) = SampleDesign();
        var glassBefore = Glass(frame);

        var ex = Assert.Throws<DesignValidationException>(() => FrameEditor.Resize(frame, 550, 1500, Rules));

        Assert.StartsWith("Cannot resize the frame", ex.Message);
        Assert.Equal(1200.0, frame.Width);
        Assert.Equal(glassBefore, Glass(frame));
    }

    [Fact]
    public void MoveDivision_OutsideFrame_IsRejected()
    {
        var (frame, mullion, _) = SampleDesign();
        Assert.Throws<DesignValidationException>(() => FrameEditor.MoveDivision(frame, mullion, 1500, Rules));
        Assert.False(FrameEditor.CanMoveDivision(frame, mullion, 1500, Rules));
        Assert.True(FrameEditor.CanMoveDivision(frame, mullion, 650, Rules));
        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));   // CanMove didn't modify
    }

    [Fact]
    public void MultipleMullionsAndTransoms_MakeAGrid()
    {
        var frame = NewFrame(1800, 1500);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 1200, Rules);
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 500, Rules);
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 1000, Rules);

        Assert.Equal(9, frame.GlassPanels.Count);
        Assert.All(frame.GlassPanels, g => Assert.True(g.Boundary.Width >= Rules.MinGlassSizeMm));
    }

    [Fact]
    public void DefaultPosition_UsesWidestFreeBay()
    {
        var frame = NewFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 300, Rules);
        var second = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, null, Rules);
        Assert.Equal(735.0, Members.DivisionPosition(Division(frame, second)));   // middle of 300..1170
    }

    // ── Splitting one glass (partial divisions, T-junctions) ────────

    [Fact]
    public void SplitGlass_CreatesPartialTransom_EndingOnMullion()
    {
        var frame = NewFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var leftGlass = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();

        var transomId = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, leftGlass.Id, 750, Rules);

        var transom = Division(frame, transomId);
        Assert.Equal(new Point2D(30, 750), transom.StartPoint);   // left frame centreline
        Assert.Equal(new Point2D(600, 750), transom.EndPoint);    // mullion centreline
        Assert.Equal(3, frame.GlassPanels.Count);
    }

    [Fact]
    public void MovingMullion_DragsTheTransomThatEndsOnIt()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var leftGlass = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, leftGlass.Id, 750, Rules);

        FrameEditor.MoveDivision(frame, mullion, 800, Rules);

        Assert.Equal(new Point2D(800, 750), Division(frame, transom).EndPoint);
        Assert.Equal(3, frame.GlassPanels.Count);
        Assert.True(FrameLayout.Compute(frame, Rules).IsValid);
    }

    [Fact]
    public void DeleteDivision_RejectedWhileOthersEndOnIt_ThenAllowed()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var leftGlass = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, leftGlass.Id, 750, Rules);

        var ex = Assert.Throws<DesignValidationException>(() => FrameEditor.DeleteDivision(frame, mullion, Rules));
        Assert.Contains("Delete those first", ex.Message);

        FrameEditor.DeleteDivision(frame, transom, Rules);
        FrameEditor.DeleteDivision(frame, mullion, Rules);
        Assert.Single(frame.GlassPanels);
        Assert.Equal(4, frame.Profiles.Count);
    }

    // ── Layout validation ───────────────────────────────────────────

    [Fact]
    public void Layout_DetectsDanglingDivision()
    {
        var frame = NewFrame();
        frame.Profiles.Add(new Profile
        {
            ProfileType = ProfileType.Mullion,
            StartPoint = new Point2D(600, 30),
            EndPoint = new Point2D(600, 700),   // stops in mid-air
            Thickness = 60
        });

        var layout = FrameLayout.Compute(frame, Rules);
        Assert.False(layout.IsValid);
        Assert.Contains(layout.Errors, e => e.Contains("without meeting"));
    }

    [Fact]
    public void Layout_RejectsDiagonalMullion()
    {
        var frame = NewFrame();
        frame.Profiles.Add(new Profile
        {
            ProfileType = ProfileType.Mullion,
            StartPoint = new Point2D(500, 30),
            EndPoint = new Point2D(700, 1470),
            Thickness = 60
        });
        Assert.False(FrameLayout.Compute(frame, Rules).IsValid);
    }

    [Fact]
    public void MemberBody_IsFaceToFace()
    {
        var (frame, mullion, transom) = SampleDesign();
        // Mullion stops at the head and sill faces (60 and 1440); it isn't trimmed at the crossing transom.
        Near(new Rectangle2D(570, 60, 60, 1380), FrameLayout.GetMemberBody(frame, Division(frame, mullion)));
        Near(new Rectangle2D(60, 720, 1080, 60), FrameLayout.GetMemberBody(frame, Division(frame, transom)));
    }

    // ── Hit testing (world geometry) ────────────────────────────────

    [Fact]
    public void HitTest_FindsEachKindOfElement()
    {
        var (frame, mullion, transom) = SampleDesign();
        var project = new Project();
        project.Frames.Add(frame);

        Assert.Equal(new DesignHit(DesignElementKind.Mullion, mullion, frame.Id), FrameHitTester.HitTest(project, new Point2D(600, 300), 1));
        Assert.Equal(new DesignHit(DesignElementKind.Transom, transom, frame.Id), FrameHitTester.HitTest(project, new Point2D(300, 750), 1));
        Assert.Equal(DesignElementKind.Glass, FrameHitTester.HitTest(project, new Point2D(300, 300), 1)!.Value.Kind);
        Assert.Equal(DesignElementKind.Frame, FrameHitTester.HitTest(project, new Point2D(10, 700), 1)!.Value.Kind);
        Assert.Null(FrameHitTester.HitTest(project, new Point2D(1300, 700), 1));
    }

    [Fact]
    public void HitTest_ToleranceIsInMillimetres()
    {
        var (frame, mullion, _) = SampleDesign();
        var project = new Project();
        project.Frames.Add(frame);
        var nearMullion = new Point2D(640, 300);   // 10 mm right of the mullion face (630)

        Assert.Equal(DesignElementKind.Glass, FrameHitTester.HitTest(project, nearMullion, 5)!.Value.Kind);
        Assert.Equal(mullion, FrameHitTester.HitTest(project, nearMullion, 12)!.Value.ElementId);
    }

    [Fact]
    public void HitTest_UsesFramePosition()
    {
        var frame = FrameEditor.CreateFrame(2000, 500, 1200, 1500, Rules);
        var project = new Project();
        project.Frames.Add(frame);

        Assert.Equal(DesignElementKind.Glass, FrameHitTester.HitTest(project, new Point2D(2600, 1250), 1)!.Value.Kind);
        Assert.Null(FrameHitTester.HitTest(project, new Point2D(600, 750), 1));
    }

    // ── Snapping ────────────────────────────────────────────────────

    [Fact]
    public void Snap_ToFrameCentre()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 400, Rules);

        var snap = DivisionSnapper.Snap(frame, mullion, 598, toleranceMm: 5, incrementMm: 1);
        Assert.Equal(600.0, snap.Position);
        Assert.Equal(DivisionSnapKind.FrameCenter, snap.Kind);
    }

    [Fact]
    public void Snap_OutsideTolerance_RoundsToIncrement()
    {
        var frame = NewFrame();
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 400, Rules);

        Assert.Equal(new DivisionSnap(512, DivisionSnapKind.Increment), DivisionSnapper.Snap(frame, mullion, 511.6, 5, 1));
        Assert.Equal(new DivisionSnap(500, DivisionSnapKind.Increment), DivisionSnapper.Snap(frame, mullion, 511.6, 5, 50));
    }

    [Fact]
    public void Snap_ToBayCentre()
    {
        var frame = NewFrame();
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var second = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 900, Rules);

        var snap = DivisionSnapper.Snap(frame, second, 887, toleranceMm: 5, incrementMm: 1);
        Assert.Equal(885.0, snap.Position);   // middle of 600..1170
        Assert.Equal(DivisionSnapKind.BayCenter, snap.Kind);
    }

    // ── Dimensions ──────────────────────────────────────────────────

    [Fact]
    public void AutoDimensions_OverallAndChains()
    {
        var (frame, _, _) = SampleDesign();
        var dims = AutoDimensions.Compute(frame);

        Assert.Equal(1200.0, dims.Single(d => d.Side == DimensionSide.Top).Value);
        Assert.Equal(1500.0, dims.Single(d => d.Side == DimensionSide.Left).Value);
        Assert.Equal(new[] { 600.0, 600.0 }, dims.Where(d => d.Side == DimensionSide.Bottom).Select(d => d.Value));
        Assert.Equal(new[] { 750.0, 750.0 }, dims.Where(d => d.Side == DimensionSide.Right).Select(d => d.Value));
    }

    [Fact]
    public void AutoDimensions_FollowGeometryChanges()
    {
        var (frame, mullion, _) = SampleDesign();
        FrameEditor.MoveDivision(frame, mullion, 700, Rules);
        FrameEditor.Resize(frame, 1400, 1500, Rules);

        var dims = AutoDimensions.Compute(frame);
        Assert.Equal(1400.0, dims.Single(d => d.Side == DimensionSide.Top).Value);
        Assert.Equal(new[] { 700.0, 700.0 }, dims.Where(d => d.Side == DimensionSide.Bottom).Select(d => d.Value));
    }

    [Fact]
    public void NewFrame_HasOnlyOverallDimensions()
    {
        Assert.Equal(2, AutoDimensions.Compute(NewFrame()).Count);
    }

    // ── Commands, undo/redo ─────────────────────────────────────────

    [Fact]
    public void Commands_UndoRedo_RestoreExactState()
    {
        var project = new Project();
        var history = new CommandHistory();
        var create = CreateFrameCommand.Create(project, 0, 0, 1200, 1500, Rules);
        history.Execute(create);
        var frame = create.Frame;

        var addMullion = AddDivisionCommand.Mullion(frame, Rules);
        history.Execute(addMullion);
        history.Execute(AddDivisionCommand.Transom(frame, Rules));
        history.Execute(new MoveDivisionCommand(frame, addMullion.CreatedProfileId!.Value, 700, Rules));
        history.Execute(new ResizeFrameCommand(frame, 1400, 1600, Rules));
        string final = ProjectSerializer.Serialize(project);

        history.Undo();   // resize
        Assert.Equal(1200.0, frame.Width);
        history.Undo();   // move
        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, addMullion.CreatedProfileId!.Value)));
        history.Undo();   // transom
        Assert.Equal(2, frame.GlassPanels.Count);
        history.Undo();   // mullion
        Assert.Single(frame.GlassPanels);
        history.Undo();   // create
        Assert.Empty(project.Frames);

        for (int i = 0; i < 5; i++) history.Redo();
        Assert.Equal(final, ProjectSerializer.Serialize(project));
    }

    [Fact]
    public void FailedCommand_IsNotRecorded()
    {
        var frame = NewFrame();
        var history = new CommandHistory();

        Assert.Throws<DesignValidationException>(() => history.Execute(new ResizeFrameCommand(frame, -5, 1500, Rules)));

        Assert.False(history.CanUndo);
        Assert.Equal(1200.0, frame.Width);
    }

    [Fact]
    public void FrameSnapshot_PreservesIds_AndIsIndependent()
    {
        var (frame, mullion, _) = SampleDesign();
        var snapshot = FrameSnapshot.Capture(frame);

        FrameEditor.MoveDivision(frame, mullion, 800, Rules);
        snapshot.ApplyTo(frame);

        Assert.Equal(600.0, Members.DivisionPosition(Division(frame, mullion)));
        Assert.Equal(snapshot.ToFrame().Profiles.Select(p => p.Id), frame.Profiles.Select(p => p.Id));
    }

    // ── Integration: consumers see plain domain data ────────────────

    [Fact]
    public void DesignedFrame_RoundTripsThroughJson()
    {
        var (frame, _, _) = SampleDesign();
        var project = new Project();
        project.Frames.Add(frame);

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)).Frames.Single();

        IReadOnlyList<Profile> profiles = loaded.Profiles;
        IReadOnlyList<GlassPanel> glass = loaded.GlassPanels;
        Assert.Equal(6, profiles.Count);
        Assert.Equal(4, glass.Count);
        Assert.True(FrameLayout.Compute(loaded, Rules).IsValid);
    }
}
