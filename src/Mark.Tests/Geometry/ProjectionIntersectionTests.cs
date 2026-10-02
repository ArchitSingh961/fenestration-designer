using Mark.Core.Geometry;
using Xunit;
using static Mark.Tests.GeometryAssert;

namespace Mark.Tests.Geometry;

public class ProjectionTests
{
    private static readonly LineSegment2D Transom = new(0, 900, 1200, 900);

    [Fact]
    public void OntoSegment_Interior()
    {
        var result = Projection2D.ProjectPointOntoSegment(new Point2D(300, 850), Transom);
        Near(new Point2D(300, 900), result.Point);
        Near(0.25, result.T);
        Near(50.0, result.Distance);
    }

    [Fact]
    public void OntoSegment_ClampsBeyondEnds()
    {
        var before = Projection2D.ProjectPointOntoSegment(new Point2D(-300, 900), Transom);
        Near(Transom.Start, before.Point);
        Near(0.0, before.T);
        Near(300.0, before.Distance);

        var after = Projection2D.ProjectPointOntoSegment(new Point2D(1500, 1300), Transom);
        Near(Transom.End, after.Point);
        Near(1.0, after.T);
        Near(500.0, after.Distance);
    }

    [Fact]
    public void OntoLine_ExtendsBeyondEnds()
    {
        var result = Projection2D.ProjectPointOntoLine(new Point2D(-300, 1000), Transom);
        Near(new Point2D(-300, 900), result.Point);
        Near(-0.25, result.T);
        Near(100.0, result.Distance);
    }

    [Fact]
    public void OntoDiagonal()
    {
        var diagonal = new LineSegment2D(0, 0, 100, 100);
        var result = Projection2D.ProjectPointOntoSegment(new Point2D(100, 0), diagonal);
        Near(new Point2D(50, 50), result.Point);
        Near(0.5, result.T);
        Near(Math.Sqrt(5000), result.Distance);
    }

    [Fact]
    public void OntoDegenerate_ReturnsStart()
    {
        var point = new LineSegment2D(10, 10, 10, 10);
        var result = Projection2D.ProjectPointOntoLine(new Point2D(13, 14), point);
        Near(new Point2D(10, 10), result.Point);
        Near(0.0, result.T);
        Near(5.0, result.Distance);
    }
}

public class IntersectionTests
{
    // A 1200 × 1500 frame with a mullion at X = 600 and a transom at Y = 900.
    private static readonly LineSegment2D Mullion = new(600, 0, 600, 1500);
    private static readonly LineSegment2D Transom = new(0, 900, 1200, 900);

    [Fact]
    public void HorizontalCrossVertical()
    {
        var hit = Intersection2D.IntersectSegments(Transom, Mullion);
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(600, 900), hit.Point);
        Near(0.5, hit.ParameterA);
        Near(0.6, hit.ParameterB);
    }

    [Fact]
    public void IsSymmetric()
    {
        var ab = Intersection2D.IntersectSegments(Transom, Mullion);
        var ba = Intersection2D.IntersectSegments(Mullion, Transom);
        Near(ab.Point, ba.Point);
        Near(ab.ParameterA, ba.ParameterB);
    }

    [Fact]
    public void DiagonalCross()
    {
        var hit = Intersection2D.IntersectSegments(new LineSegment2D(0, 0, 100, 100), new LineSegment2D(0, 100, 100, 0));
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(50, 50), hit.Point);
    }

    [Fact]
    public void ParallelOffset_None()
    {
        var hit = Intersection2D.IntersectSegments(Transom, new LineSegment2D(0, 950, 1200, 950));
        Assert.Equal(SegmentIntersectionKind.None, hit.Kind);
        Assert.False(hit.Intersects);
    }

    [Fact]
    public void NonIntersecting_LinesWouldCross_ButSegmentsDoNot()
    {
        // The infinite lines cross at (600, 900) but the short transom stops at X = 500.
        var shortTransom = new LineSegment2D(0, 900, 500, 900);
        Assert.Equal(SegmentIntersectionKind.None, Intersection2D.IntersectSegments(shortTransom, Mullion).Kind);

        var line = Intersection2D.IntersectLines(shortTransom, Mullion);
        Assert.NotNull(line);
        Near(new Point2D(600, 900), line.Value.Point);
        Near(1.2, line.Value.ParameterA);   // beyond the segment's end
    }

    [Fact]
    public void EndpointTouch_TJunction()
    {
        // A transom that ends exactly on the mullion.
        var hit = Intersection2D.IntersectSegments(new LineSegment2D(0, 900, 600, 900), Mullion);
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(600, 900), hit.Point);
        Near(1.0, hit.ParameterA);
    }

    [Fact]
    public void EndpointTouch_SharedCorner()
    {
        var top = new LineSegment2D(0, 0, 1200, 0);
        var right = new LineSegment2D(1200, 0, 1200, 1500);
        var hit = Intersection2D.IntersectSegments(top, right);
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(1200, 0), hit.Point);
    }

    [Fact]
    public void NearMiss_WithinTolerance_Touches_OutsideDoesNot()
    {
        var almost = new LineSegment2D(0, 900, 599.95, 900);   // 0.05 mm short of the mullion
        Assert.Equal(SegmentIntersectionKind.Point, Intersection2D.IntersectSegments(almost, Mullion).Kind);

        var gap = new LineSegment2D(0, 900, 599, 900);          // 1 mm short
        Assert.Equal(SegmentIntersectionKind.None, Intersection2D.IntersectSegments(gap, Mullion).Kind);
        Assert.Equal(SegmentIntersectionKind.Point, Intersection2D.IntersectSegments(gap, Mullion, tolerance: 2).Kind);
    }

    [Fact]
    public void Collinear_Overlapping()
    {
        var a = new LineSegment2D(0, 0, 1000, 0);
        var b = new LineSegment2D(600, 0, 1500, 0);
        var hit = Intersection2D.IntersectSegments(a, b);
        Assert.Equal(SegmentIntersectionKind.Overlap, hit.Kind);
        Near(new Point2D(600, 0), hit.Overlap.Start);
        Near(new Point2D(1000, 0), hit.Overlap.End);
        Near(0.6, hit.ParameterA);
        Near(0.0, hit.ParameterB);
    }

    [Fact]
    public void Collinear_OppositeDirections_Overlap()
    {
        var a = new LineSegment2D(0, 0, 1000, 0);
        var b = new LineSegment2D(800, 0, 200, 0);
        var hit = Intersection2D.IntersectSegments(a, b);
        Assert.Equal(SegmentIntersectionKind.Overlap, hit.Kind);
        Near(new Point2D(200, 0), hit.Overlap.Start);
        Near(new Point2D(800, 0), hit.Overlap.End);
    }

    [Fact]
    public void Collinear_EndToEnd_IsSinglePoint()
    {
        var hit = Intersection2D.IntersectSegments(new LineSegment2D(0, 0, 600, 0), new LineSegment2D(600, 0, 1200, 0));
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(600, 0), hit.Point);
    }

    [Fact]
    public void Collinear_Disjoint_None()
    {
        var hit = Intersection2D.IntersectSegments(new LineSegment2D(0, 0, 500, 0), new LineSegment2D(700, 0, 1200, 0));
        Assert.Equal(SegmentIntersectionKind.None, hit.Kind);
    }

    [Fact]
    public void DegenerateSegment_OnOtherSegment_IsPoint()
    {
        var point = new LineSegment2D(600, 300, 600, 300);
        var hit = Intersection2D.IntersectSegments(point, Mullion);
        Assert.Equal(SegmentIntersectionKind.Point, hit.Kind);
        Near(new Point2D(600, 300), hit.Point);
        Near(0.2, hit.ParameterB);
    }

    [Fact]
    public void InfiniteLines_ParallelOrCoincident_ReturnNull()
    {
        Assert.Null(Intersection2D.IntersectLines(Transom, new LineSegment2D(0, 950, 10, 950)));
        Assert.Null(Intersection2D.IntersectLines(Transom, new LineSegment2D(2000, 900, 3000, 900)));
    }

    [Fact]
    public void InfiniteLines_DegenerateThrows()
    {
        Assert.Throws<ArgumentException>(() => Intersection2D.IntersectLines(Transom, new LineSegment2D(1, 1, 1, 1)));
    }

    // ── Segment × rectangle ────────────────────────────────────────

    private static readonly Rectangle2D Frame = new(0, 0, 1200, 1500);

    [Fact]
    public void SegmentThroughRectangle_TwoBoundaryPoints_Ordered()
    {
        var through = new LineSegment2D(-100, 750, 1300, 750);
        var points = Intersection2D.IntersectSegmentWithRectangle(through, Frame);
        Assert.Equal(2, points.Count);
        Near(new Point2D(0, 750), points[0]);
        Near(new Point2D(1200, 750), points[1]);
    }

    [Fact]
    public void SegmentFromInside_OneBoundaryPoint()
    {
        var points = Intersection2D.IntersectSegmentWithRectangle(new LineSegment2D(600, 750, 600, 2000), Frame);
        Near(new Point2D(600, 1500), Assert.Single(points));
    }

    [Fact]
    public void SegmentThroughCorner_NotDuplicated()
    {
        var diagonal = new LineSegment2D(-100, -100, 600, 600);
        var points = Intersection2D.IntersectSegmentWithRectangle(diagonal, Frame);
        Near(new Point2D(0, 0), Assert.Single(points));
    }

    [Fact]
    public void SegmentAlongEdge_ReturnsSharedStretch()
    {
        var alongTop = new LineSegment2D(-200, 0, 500, 0);
        var points = Intersection2D.IntersectSegmentWithRectangle(alongTop, Frame);
        Assert.Equal(2, points.Count);
        Near(new Point2D(0, 0), points[0]);
        Near(new Point2D(500, 0), points[1]);
    }

    [Fact]
    public void SegmentOutside_NoPoints()
    {
        Assert.Empty(Intersection2D.IntersectSegmentWithRectangle(new LineSegment2D(1300, 0, 1300, 1500), Frame));
    }

    [Fact]
    public void Clip_SegmentToRectangle()
    {
        var clipped = Intersection2D.ClipSegmentToRectangle(new LineSegment2D(-100, 750, 1300, 750), Frame);
        Assert.NotNull(clipped);
        Near(new Point2D(0, 750), clipped.Value.Start);
        Near(new Point2D(1200, 750), clipped.Value.End);

        var inside = new LineSegment2D(100, 100, 200, 200);
        Assert.Equal(inside, Intersection2D.ClipSegmentToRectangle(inside, Frame));

        Assert.Null(Intersection2D.ClipSegmentToRectangle(new LineSegment2D(1300, 0, 1400, 100), Frame));
    }
}
