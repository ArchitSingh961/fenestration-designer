using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class LineSegment2DTests
{
    [Fact]
    public void HorizontalLength()
    {
        var seg = new LineSegment2D(0, 0, 1200, 0);
        Near(1200.0, seg.Length);
        Near(1_440_000.0, seg.LengthSquared);
        Near(Vector2D.UnitX, seg.Direction);
    }

    [Fact]
    public void VerticalLength()
    {
        var seg = new LineSegment2D(0, 0, 0, 1500);
        Near(1500.0, seg.Length);
        Near(Vector2D.UnitY, seg.Direction);
    }

    [Fact]
    public void DiagonalLength()
    {
        var seg = new LineSegment2D(100, 100, 400, 500);
        Near(500.0, seg.Length);
        Near(new Vector2D(0.6, 0.8), seg.Direction);
    }

    [Fact]
    public void Midpoint()
    {
        Near(new Point2D(600, 750), new LineSegment2D(0, 0, 1200, 1500).Midpoint);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 375.0)]
    [InlineData(0.5, 750.0)]
    [InlineData(1.0, 1500.0)]
    public void PointAt(double t, double expectedY)
    {
        Near(new Point2D(0, expectedY), new LineSegment2D(0, 0, 0, 1500).PointAt(t));
    }

    [Fact]
    public void ClosestPoint_ProjectsInside_AndClampsToEnds()
    {
        var seg = new LineSegment2D(0, 0, 100, 0);
        Near(new Point2D(50, 0), seg.ClosestPoint(new Point2D(50, 30)));
        Near(new Point2D(0, 0), seg.ClosestPoint(new Point2D(-40, 10)));
        Near(new Point2D(100, 0), seg.ClosestPoint(new Point2D(500, 10)));
    }

    [Fact]
    public void DistanceTo()
    {
        var seg = new LineSegment2D(0, 0, 100, 0);
        Near(30.0, seg.DistanceTo(new Point2D(50, 30)));
        Near(5.0, seg.DistanceTo(new Point2D(103, 4)));   // nearest is the End point
        Near(0.0, seg.DistanceTo(new Point2D(25, 0)));
    }

    [Fact]
    public void ContainsPoint_UsesTolerance()
    {
        var seg = new LineSegment2D(0, 0, 1200, 0);
        Assert.True(seg.ContainsPoint(new Point2D(600, 0)));
        Assert.True(seg.ContainsPoint(new Point2D(600, 0.05)));          // within default 0.1 mm
        Assert.False(seg.ContainsPoint(new Point2D(600, 0.5)));
        Assert.True(seg.ContainsPoint(new Point2D(600, 0.5), tolerance: 1.0));
        Assert.False(seg.ContainsPoint(new Point2D(1201, 0)));
    }

    [Fact]
    public void ContainsPoint_RejectsNegativeTolerance()
    {
        var seg = new LineSegment2D(0, 0, 10, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => seg.ContainsPoint(Point2D.Origin, -1));
    }

    [Fact]
    public void Degenerate_Segment_BehavesAsPoint()
    {
        var seg = new LineSegment2D(5, 5, 5, 5);
        Assert.True(seg.IsDegenerate());
        Assert.Equal(Vector2D.Zero, seg.Direction);
        Near(5.0, seg.DistanceTo(new Point2D(8, 9)));
    }

    [Fact]
    public void Bounds_EncloseBothEnds()
    {
        var box = new LineSegment2D(400, 10, 100, 500).Bounds;
        Near(new Rectangle2D(100, 10, 300, 490), box.ToRectangle());
    }
}
