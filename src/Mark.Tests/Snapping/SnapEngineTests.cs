using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Interfaces;
using Mark.Core.Models;
using Mark.Core.Snapping;
using Xunit;
using static Mark.Tests.GeometryAssert;

namespace Mark.Tests.Snapping;

/// <summary>
/// Scene: 1200 × 1500 frame at the origin, 60 mm profiles, mullion at X 600, transom at Y 750.
/// Centrelines: frame loop 30..1170 × 30..1470; the mullion/transom cross at (600, 750).
/// </summary>
public class SnapEngineTests
{
    private static readonly DesignRules Rules = new();

    private static (Project Project, Frame Frame, Guid Mullion, Guid Transom) Scene()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, null, 750, Rules);
        var project = new Project();
        project.Frames.Add(frame);
        return (project, frame, mullion, transom);
    }

    private static SnapSession Session(Project project, SnapSettings? settings = null, params Guid[] exclude)
        => new SnapEngine(settings ?? new SnapSettings(), Rules).BeginSession(project, exclude);

    // ── Individual snap types ───────────────────────────────────────

    [Fact]
    public void GridSnap_598_To_600()
    {
        var (project, _, _, _) = Scene();
        var settings = new SnapSettings { ObjectSnapEnabled = false, GridEnabled = true, GridSpacingMm = 10 };
        var session = Session(project, settings);

        var axis = session.SnapCoordinate(SnapAxis.X, 598, toleranceMm: 5);
        Assert.Equal(SnapType.Grid, axis.Type);
        Near(600.0, axis.Value);

        var point = session.SnapPoint(new Point2D(598, 1003), toleranceMm: 5);
        Assert.Equal(SnapType.Grid, point.Type);
        Near(new Point2D(600, 1000), point.Point);
    }

    [Fact]
    public void EndpointSnap_ToFrameCorner()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapPoint(new Point2D(1198, 3), toleranceMm: 5);
        Assert.Equal(SnapType.Endpoint, result.Type);
        Near(new Point2D(1200, 0), result.Point);
    }

    [Fact]
    public void MidpointSnap_ToEdgeMidpoint()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapPoint(new Point2D(601, 2), toleranceMm: 5);
        Assert.Equal(SnapType.Midpoint, result.Type);
        Near(new Point2D(600, 0), result.Point);
    }

    [Fact]
    public void IntersectionSnap_ToMullionTransomCrossing()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapPoint(new Point2D(602, 748), toleranceMm: 5);
        Assert.Equal(SnapType.Intersection, result.Type);
        Near(new Point2D(600, 750), result.Point);
    }

    [Fact]
    public void EdgeSnap_ProjectsOntoStructuralEdge()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapPoint(new Point2D(1197, 400), toleranceMm: 5);
        Assert.Equal(SnapType.Edge, result.Type);
        Near(new Point2D(1200, 400), result.Point);
    }

    [Fact]
    public void CenterSnap_ToOpeningCentre()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapPoint(new Point2D(318, 386), toleranceMm: 10);
        Assert.Equal(SnapType.Center, result.Type);
        Near(new Point2D(315, 390), result.Point);   // centre of the 30..600 × 30..750 bay
    }

    // ── Tolerance & priority ────────────────────────────────────────

    [Fact]
    public void OutsideTolerance_DoesNotSnap()
    {
        var (project, _, _, _) = Scene();
        var session = Session(project);
        var point = new Point2D(1190, 400);   // 10 mm from the right edge

        var none = session.SnapPoint(point, toleranceMm: 5);
        Assert.Equal(SnapType.None, none.Type);
        Assert.Equal(point, none.Point);

        Assert.Equal(SnapType.Edge, session.SnapPoint(point, toleranceMm: 12).Type);
    }

    [Fact]
    public void Priority_IntersectionBeatsACloserEndpoint()
    {
        var (project, _, _, _) = Scene();
        // (0, 0) is an endpoint 20.6 mm away; the frame centreline corner (30, 30) is an intersection 26.9 mm away.
        var result = Session(project).SnapPoint(new Point2D(20, 5), toleranceMm: 50);
        Assert.Equal(SnapType.Intersection, result.Type);
        Near(new Point2D(30, 30), result.Point);
    }

    [Fact]
    public void Priority_IsConfigurable()
    {
        var (project, _, _, _) = Scene();
        var settings = new SnapSettings { Priority = new[] { SnapType.Endpoint, SnapType.Intersection } };
        var result = Session(project, settings).SnapPoint(new Point2D(20, 5), toleranceMm: 50);
        Assert.Equal(SnapType.Endpoint, result.Type);
        Near(new Point2D(0, 0), result.Point);
    }

    [Fact]
    public void GridIsLowestPriority()
    {
        var (project, _, _, _) = Scene();
        var settings = new SnapSettings { GridEnabled = true, GridSpacingMm = 10 };
        var result = Session(project, settings).SnapPoint(new Point2D(1198, 3), toleranceMm: 5);
        Assert.Equal(SnapType.Endpoint, result.Type);
    }

    [Fact]
    public void ToleranceIsConfigurableInPixels()
    {
        var settings = new SnapSettings { TolerancePixels = 8 };
        Near(16.0, settings.ToleranceMm(zoom: 0.5));
        Near(4.0, settings.ToleranceMm(zoom: 2));
        settings.TolerancePixels = 12;
        Near(24.0, settings.ToleranceMm(zoom: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.ToleranceMm(0));
    }

    [Fact]
    public void ObjectSnapOff_AndGridOff_NeverSnaps()
    {
        var (project, _, _, _) = Scene();
        var session = Session(project, new SnapSettings { ObjectSnapEnabled = false });
        Assert.Equal(0, session.TargetCount);
        Assert.Equal(SnapType.None, session.SnapPoint(new Point2D(1199, 1), 50).Type);
    }

    [Fact]
    public void DisabledTypeIsSkipped()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project, new SnapSettings { IntersectionEnabled = false }).SnapPoint(new Point2D(602, 748), 5);
        Assert.Equal(SnapType.Midpoint, result.Type);   // the mullion's centreline midpoint is also (600, 750)
    }

    // ── One-axis snapping and exclusion ─────────────────────────────

    [Fact]
    public void AxisSnap_MeasuresAlongTheAxisOnly()
    {
        var (project, _, _, _) = Scene();
        var result = Session(project).SnapCoordinate(SnapAxis.X, 597, toleranceMm: 5);
        Assert.True(result.IsSnapped);
        Near(600.0, result.Value);
        Assert.NotNull(result.Target);
    }

    [Fact]
    public void MovingObject_IsNotItsOwnTarget()
    {
        var (project, _, mullion, _) = Scene();
        var session = Session(project, exclude: mullion);

        Assert.DoesNotContain(session.Targets(SnapType.Intersection), t => Math.Abs(t.Point.X - 600) < 0.1);
        Assert.DoesNotContain(session.Targets(SnapType.Endpoint), t => t.Point == new Point2D(600, 30));

        var result = session.SnapCoordinate(SnapAxis.X, 597, 5);   // the frame's own midlines still count
        Assert.Equal(SnapType.Midpoint, result.Type);
        Near(600.0, result.Value);
    }

    [Fact]
    public void DependentGeometry_IsExcludedToo()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var left = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        FrameEditor.AddDivision(frame, MemberAxis.Horizontal, left.Id, 750, Rules);   // ends on the mullion
        var project = new Project();
        project.Frames.Add(frame);

        var session = Session(project, exclude: mullion);

        // The partial transom moves with the mullion, so its midpoint and its end on the mullion are stale.
        Assert.DoesNotContain(session.Targets(SnapType.Midpoint), t => t.Point == new Point2D(315, 750));
        Assert.DoesNotContain(session.Targets(SnapType.Endpoint), t => t.Point == new Point2D(600, 750));
    }

    [Fact]
    public void BayCentre_IsComputedWithoutTheMovingDivision()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 300, Rules);
        var moving = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 900, Rules);
        var project = new Project();
        project.Frames.Add(frame);

        var result = Session(project, exclude: moving).SnapCoordinate(SnapAxis.X, 738, 5);
        Assert.Equal(SnapType.Center, result.Type);
        Near(735.0, result.Value);   // middle of the 300..1170 bay
    }

    [Fact]
    public void LegacyISnapProvider_Adapter()
    {
        var (project, _, _, _) = Scene();
        ISnapProvider provider = new ProjectSnapProvider(new SnapEngine(new SnapSettings(), Rules), () => project);

        var hit = provider.FindSnap(new Point2D(1198, 3), 5);
        Assert.NotNull(hit);
        Assert.Equal(SnapType.Endpoint, hit!.Type);
        Assert.Null(provider.FindSnap(new Point2D(5000, 5000), 5));

        provider.IsEnabled = false;
        Assert.Null(provider.FindSnap(new Point2D(1198, 3), 5));
    }
}
