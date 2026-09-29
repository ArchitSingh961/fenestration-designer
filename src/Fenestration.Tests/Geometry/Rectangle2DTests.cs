using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class Rectangle2DTests
{
    private static readonly Rectangle2D Frame = new(0, 0, 1200, 1500);

    [Fact]
    public void Edges_1200x1500()
    {
        Near(0.0, Frame.Left);
        Near(1200.0, Frame.Right);
        Near(0.0, Frame.Top);
        Near(1500.0, Frame.Bottom);
        Near(1_800_000.0, Frame.Area);
    }

    [Fact]
    public void Corners_And_Center()
    {
        var r = new Rectangle2D(100, 200, 1200, 1500);
        Near(new Point2D(100, 200), r.TopLeft);
        Near(new Point2D(1300, 200), r.TopRight);
        Near(new Point2D(100, 1700), r.BottomLeft);
        Near(new Point2D(1300, 1700), r.BottomRight);
        Near(new Point2D(700, 950), r.Center);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void Constructor_RejectsInvalidSize(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle2D(0, 0, width, height));
    }

    [Fact]
    public void Constructor_RejectsNonFinitePosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle2D(double.NaN, 0, 10, 10));
    }

    [Fact]
    public void FromCorners_Normalizes()
    {
        var r = Rectangle2D.FromCorners(new Point2D(1200, 1500), new Point2D(0, 0));
        Near(Frame, r);
        Assert.True(r.Width >= 0 && r.Height >= 0);
    }

    [Fact]
    public void Contains_IsInclusiveOfEdges()
    {
        Assert.True(Frame.Contains(new Point2D(600, 750)));
        Assert.True(Frame.Contains(new Point2D(1200, 1500)));
        Assert.False(Frame.Contains(new Point2D(1200.01, 750)));
    }

    [Fact]
    public void Contains_WithTolerance()
    {
        Assert.True(Frame.Contains(new Point2D(1200.05, 750), GeometryTolerance.Default));
        Assert.False(Frame.Contains(new Point2D(1200.5, 750), GeometryTolerance.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Frame.Contains(Point2D.Origin, -0.1));
    }

    [Fact]
    public void Contains_Rectangle()
    {
        Assert.True(Frame.Contains(new Rectangle2D(50, 50, 500, 500)));
        Assert.True(Frame.Contains(Frame));
        Assert.False(Frame.Contains(new Rectangle2D(1000, 50, 500, 500)));
    }

    [Fact]
    public void Intersection_Overlapping()
    {
        var other = new Rectangle2D(600, 900, 1000, 1000);
        Assert.True(Frame.Intersects(other));
        Rectangle2D? overlap = Frame.Intersection(other);
        Assert.NotNull(overlap);
        Near(new Rectangle2D(600, 900, 600, 600), overlap.Value);
    }

    [Fact]
    public void Intersection_IsSymmetric()
    {
        var other = new Rectangle2D(-100, 300, 400, 200);
        Near(Frame.Intersection(other)!.Value, other.Intersection(Frame)!.Value);
    }

    [Fact]
    public void Intersection_Touching_IsZeroWidth()
    {
        var neighbour = new Rectangle2D(1200, 0, 800, 1500);
        Assert.True(Frame.Intersects(neighbour));
        Rectangle2D? overlap = Frame.Intersection(neighbour);
        Assert.NotNull(overlap);
        Near(0.0, overlap.Value.Width);
        Near(1500.0, overlap.Value.Height);
    }

    [Fact]
    public void Intersection_Disjoint_IsNull()
    {
        var far = new Rectangle2D(2000, 2000, 100, 100);
        Assert.False(Frame.Intersects(far));
        Assert.Null(Frame.Intersection(far));
    }

    [Fact]
    public void Intersection_Contained_IsInner()
    {
        var inner = new Rectangle2D(100, 100, 200, 300);
        Near(inner, Frame.Intersection(inner)!.Value);
    }

    [Fact]
    public void Offset_KeepsSize()
    {
        Near(new Rectangle2D(5, -5, 10, 20), new Rectangle2D(0, 0, 10, 20).Offset(new Vector2D(5, -5)));
        Near(new Rectangle2D(5, -5, 10, 20), new Rectangle2D(0, 0, 10, 20).Offset(5, -5));
    }

    [Fact]
    public void Inflate_GrowsAndShrinksAboutCenter()
    {
        var r = new Rectangle2D(100, 100, 200, 100);
        Near(new Rectangle2D(90, 90, 220, 120), r.Inflate(10));
        Near(new Rectangle2D(110, 105, 180, 90), r.Inflate(-10, -5));
        Near(r.Center, r.Inflate(25).Center);
    }

    [Fact]
    public void Inflate_BeyondZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rectangle2D(0, 0, 10, 10).Inflate(-6));
    }

    [Fact]
    public void Edges_FormClosedClockwiseLoop()
    {
        LineSegment2D[] edges = Frame.GetEdges();
        for (int i = 0; i < edges.Length; i++)
            Near(edges[i].End, edges[(i + 1) % edges.Length].Start);
    }
}
