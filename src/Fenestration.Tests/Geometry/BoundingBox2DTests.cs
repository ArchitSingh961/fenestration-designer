using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class BoundingBox2DTests
{
    [Fact]
    public void Default_IsEmpty_AndExtentsThrow()
    {
        BoundingBox2D box = default;
        Assert.True(box.IsEmpty);
        Assert.Equal(BoundingBox2D.Empty, box);
        Assert.Throws<InvalidOperationException>(() => box.MinX);
        Assert.Throws<InvalidOperationException>(() => box.Center);
    }

    [Fact]
    public void FromPoints()
    {
        var box = BoundingBox2D.FromPoints(new Point2D(10, 50), new Point2D(-20, 5), new Point2D(30, 25));
        Near(-20.0, box.MinX);
        Near(5.0, box.MinY);
        Near(30.0, box.MaxX);
        Near(50.0, box.MaxY);
        Near(50.0, box.Width);
        Near(45.0, box.Height);
        Near(new Point2D(5, 27.5), box.Center);
    }

    [Fact]
    public void FromPoints_EmptySequence_IsEmpty()
    {
        Assert.True(BoundingBox2D.FromPoints(Array.Empty<Point2D>()).IsEmpty);
    }

    [Fact]
    public void FromSegment_And_FromRectangle()
    {
        Near(new Rectangle2D(0, 0, 1200, 0), BoundingBox2D.FromSegment(new LineSegment2D(1200, 0, 0, 0)).ToRectangle());
        Near(new Rectangle2D(0, 0, 1200, 1500), BoundingBox2D.FromRectangle(new Rectangle2D(0, 0, 1200, 1500)).ToRectangle());
    }

    [Fact]
    public void FromCollections()
    {
        var fromSegments = BoundingBox2D.FromSegments(new[]
        {
            new LineSegment2D(600, 0, 600, 1500),
            new LineSegment2D(0, 900, 1200, 900)
        });
        Near(new Rectangle2D(0, 0, 1200, 1500), fromSegments.ToRectangle());

        var fromRects = BoundingBox2D.FromRectangles(new[]
        {
            new Rectangle2D(0, 0, 1200, 1500),
            new Rectangle2D(1300, 0, 800, 2100)
        });
        Near(new Rectangle2D(0, 0, 2100, 2100), fromRects.ToRectangle());
    }

    [Fact]
    public void Expand_ByPoint_And_Margin()
    {
        var box = BoundingBox2D.Empty.Expand(new Point2D(0, 0)).Expand(new Point2D(100, 50));
        Near(new Rectangle2D(0, 0, 100, 50), box.ToRectangle());
        Near(new Rectangle2D(-10, -10, 120, 70), box.Expand(10).ToRectangle());
        Assert.True(BoundingBox2D.Empty.Expand(10).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => box.Expand(-30));
    }

    [Fact]
    public void Expand_RejectsNonFinitePoint()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox2D.Empty.Expand(new Point2D(double.NaN, 0)));
    }

    [Fact]
    public void Union()
    {
        var a = BoundingBox2D.FromMinMax(0, 0, 10, 10);
        var b = BoundingBox2D.FromMinMax(5, -5, 20, 8);
        Near(new Rectangle2D(0, -5, 20, 15), a.Union(b).ToRectangle());
        Assert.Equal(a, a.Union(BoundingBox2D.Empty));
        Assert.Equal(a, BoundingBox2D.Empty.Union(a));
    }

    [Fact]
    public void Contains_And_Intersects()
    {
        var box = BoundingBox2D.FromMinMax(0, 0, 100, 100);
        Assert.True(box.Contains(new Point2D(100, 50)));
        Assert.False(box.Contains(new Point2D(100.05, 50)));
        Assert.True(box.Contains(new Point2D(100.05, 50), GeometryTolerance.Default));
        Assert.True(box.Contains(BoundingBox2D.FromMinMax(10, 10, 20, 20)));
        Assert.True(box.Intersects(BoundingBox2D.FromMinMax(100, 100, 200, 200)));  // touching corner
        Assert.False(box.Intersects(BoundingBox2D.FromMinMax(101, 0, 200, 100)));
        Assert.False(BoundingBox2D.Empty.Intersects(box));
        Assert.False(BoundingBox2D.Empty.Contains(Point2D.Origin));
    }

    [Fact]
    public void FromMinMax_RejectsInverted()
    {
        Assert.Throws<ArgumentException>(() => BoundingBox2D.FromMinMax(10, 0, 0, 10));
    }
}
