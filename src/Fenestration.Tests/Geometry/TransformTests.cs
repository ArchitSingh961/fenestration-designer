using Fenestration.Core.Geometry;
using Xunit;
using static Fenestration.Tests.GeometryAssert;

namespace Fenestration.Tests.Geometry;

public class Transform2DTests
{
    [Fact]
    public void Identity_LeavesPointsUnchanged()
    {
        Near(new Point2D(12, 34), Transform2D.Identity.TransformPoint(new Point2D(12, 34)));
    }

    [Fact]
    public void Translation_MovesPoints_NotVectors()
    {
        var t = Transform2D.Translation(100, -50);
        Near(new Point2D(1300, 1450), t.TransformPoint(new Point2D(1200, 1500)));
        Near(new Vector2D(3, 4), t.TransformVector(new Vector2D(3, 4)));
    }

    [Fact]
    public void Scale_AboutOrigin_And_AboutCenter()
    {
        Near(new Point2D(2400, 750), Transform2D.Scale(2, 0.5).TransformPoint(new Point2D(1200, 1500)));

        var center = new Point2D(600, 750);
        var aboutCenter = Transform2D.Scale(2, 2, center);
        Near(center, aboutCenter.TransformPoint(center));
        Near(new Point2D(-600, -750), aboutCenter.TransformPoint(Point2D.Origin));
    }

    [Fact]
    public void Rotation90_TurnsXTowardsY_Exactly()
    {
        var r = Transform2D.Rotation(90);
        // Exact for multiples of 90° — no 6e-17 residue.
        Assert.Equal(new Point2D(0, 100), r.TransformPoint(new Point2D(100, 0)));
        Assert.Equal(new Vector2D(-1, 0), r.TransformVector(Vector2D.UnitY));
    }

    [Fact]
    public void Rotation_ArbitraryAngle()
    {
        var p = Transform2D.Rotation(30).TransformPoint(new Point2D(100, 0));
        Near(new Point2D(100 * Math.Cos(Math.PI / 6), 100 * Math.Sin(Math.PI / 6)), p);
        Near(100.0, p.DistanceTo(Point2D.Origin));
    }

    [Fact]
    public void Rotation_AboutCenter_KeepsCenterFixed()
    {
        var center = new Point2D(600, 750);
        var r = Transform2D.Rotation(90, center);
        Near(center, r.TransformPoint(center));
        // The frame's top-left corner swings to the top-right of the rotated footprint.
        Near(new Point2D(1350, 150), r.TransformPoint(Point2D.Origin));
    }

    [Fact]
    public void Then_AppliesInOrder()
    {
        var translateThenScale = Transform2D.Translation(10, 0).Then(Transform2D.Scale(2));
        var scaleThenTranslate = Transform2D.Scale(2).Then(Transform2D.Translation(10, 0));
        Near(new Point2D(22, 0), translateThenScale.TransformPoint(new Point2D(1, 0)));
        Near(new Point2D(12, 0), scaleThenTranslate.TransformPoint(new Point2D(1, 0)));
    }

    [Fact]
    public void Inverse_UndoesTransform()
    {
        var t = Transform2D.Rotation(37, new Point2D(10, 20))
            .Then(Transform2D.Scale(1.5, 0.75))
            .Then(Transform2D.Translation(-300, 1200));
        var inverse = t.Inverse();

        var p = new Point2D(1234.5, -678.9);
        Near(p, inverse.TransformPoint(t.TransformPoint(p)));
        Assert.True(t.Then(inverse).AlmostEquals(Transform2D.Identity));
    }

    [Fact]
    public void Singular_CannotBeInverted()
    {
        var collapse = Transform2D.Scale(0, 1);
        Assert.False(collapse.IsInvertible);
        Assert.False(collapse.TryInvert(out _));
        Assert.Throws<InvalidOperationException>(() => collapse.Inverse());
    }

    [Fact]
    public void Constructor_RejectsNaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Transform2D(double.NaN, 0, 0, 1, 0, 0));
    }

    [Fact]
    public void TransformSegment_PreservesLengthUnderRotation()
    {
        var seg = new LineSegment2D(0, 0, 0, 1500);
        var rotated = Transform2D.Rotation(-90).TransformSegment(seg);
        Near(1500.0, rotated.Length);
        Near(new Point2D(1500, 0), rotated.End);
    }
}
