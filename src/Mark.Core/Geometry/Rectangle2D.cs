using System.Globalization;

namespace Mark.Core.Geometry;

/// <summary>
/// Immutable axis-aligned rectangle in world coordinates (mm), stored in NORMALIZED form:
/// (X, Y) is the top-left corner and Width, Height are always ≥ 0.
/// Coordinate system: X → right, Y → down, so Top ≤ Bottom and Left ≤ Right.
///
/// The constructor rejects negative sizes and non-finite values. To build a rectangle from two
/// arbitrary corners (e.g. a drag), use <see cref="FromCorners"/>, which normalizes them.
/// All containment/intersection tests treat the rectangle as CLOSED (edges are inside).
/// </summary>
public readonly struct Rectangle2D : IEquatable<Rectangle2D>
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    /// <exception cref="ArgumentOutOfRangeException">A value is not finite, or a size is negative.</exception>
    public Rectangle2D(double x, double y, double width, double height)
    {
        GeometryValidation.EnsureFinite(x);
        GeometryValidation.EnsureFinite(y);
        GeometryValidation.EnsureNonNegative(width);
        GeometryValidation.EnsureNonNegative(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Builds the rectangle spanned by two opposite corners, in any order.</summary>
    public static Rectangle2D FromCorners(Point2D a, Point2D b)
    {
        GeometryValidation.EnsureFinite(a);
        GeometryValidation.EnsureFinite(b);
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        return new Rectangle2D(left, top, Math.Max(a.X, b.X) - left, Math.Max(a.Y, b.Y) - top);
    }

    // ── Edges ───────────────────────────────────────────────────────

    public double Left => X;
    public double Right => X + Width;
    public double Top => Y;
    public double Bottom => Y + Height;
    public double Area => Width * Height;

    // ── Derived points ──────────────────────────────────────────────

    public Point2D TopLeft => new(Left, Top);
    public Point2D TopRight => new(Right, Top);
    public Point2D BottomLeft => new(Left, Bottom);
    public Point2D BottomRight => new(Right, Bottom);
    public Point2D Center => new(X + Width / 2.0, Y + Height / 2.0);

    // ── Edge segments ───────────────────────────────────────────────

    public LineSegment2D TopEdge => new(TopLeft, TopRight);
    public LineSegment2D RightEdge => new(TopRight, BottomRight);
    public LineSegment2D BottomEdge => new(BottomRight, BottomLeft);
    public LineSegment2D LeftEdge => new(BottomLeft, TopLeft);

    /// <summary>The four edges in clockwise-on-screen order: top, right, bottom, left.</summary>
    public LineSegment2D[] GetEdges() => new[] { TopEdge, RightEdge, BottomEdge, LeftEdge };

    public BoundingBox2D Bounds => BoundingBox2D.FromRectangle(this);

    // ── Hit testing ─────────────────────────────────────────────────

    /// <summary>True if the point is inside or exactly on the boundary.</summary>
    public bool Contains(Point2D point) => Contains(point, 0.0);

    /// <summary>True if the point is inside, or within <paramref name="tolerance"/> mm outside, the boundary.</summary>
    public bool Contains(Point2D point, double tolerance)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);
        return point.X >= Left - tolerance && point.X <= Right + tolerance
            && point.Y >= Top - tolerance && point.Y <= Bottom + tolerance;
    }

    /// <summary>True if <paramref name="other"/> lies entirely inside this rectangle (edges may touch).</summary>
    public bool Contains(Rectangle2D other)
        => other.Left >= Left && other.Right <= Right
        && other.Top >= Top && other.Bottom <= Bottom;

    /// <summary>True if the rectangles overlap or touch.</summary>
    public bool Intersects(Rectangle2D other)
        => Left <= other.Right && Right >= other.Left
        && Top <= other.Bottom && Bottom >= other.Top;

    /// <summary>
    /// The overlapping region, or null if the rectangles are disjoint.
    /// Rectangles that only touch produce a zero-width and/or zero-height result.
    /// </summary>
    public Rectangle2D? Intersection(Rectangle2D other)
    {
        double left = Math.Max(Left, other.Left);
        double top = Math.Max(Top, other.Top);
        double right = Math.Min(Right, other.Right);
        double bottom = Math.Min(Bottom, other.Bottom);
        if (right < left || bottom < top)
            return null;
        return new Rectangle2D(left, top, right - left, bottom - top);
    }

    // ── Transforms ──────────────────────────────────────────────────

    public Rectangle2D Offset(Vector2D offset) => Offset(offset.X, offset.Y);

    public Rectangle2D Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public Rectangle2D WithSize(double width, double height) => new(X, Y, width, height);

    public Rectangle2D WithPosition(double x, double y) => new(x, y, Width, Height);

    /// <summary>Grows every side by <paramref name="margin"/> mm (negative shrinks).</summary>
    public Rectangle2D Inflate(double margin) => Inflate(margin, margin);

    /// <summary>
    /// Grows left/right by <paramref name="dx"/> and top/bottom by <paramref name="dy"/> (negative shrinks).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Shrinking would produce a negative size.</exception>
    public Rectangle2D Inflate(double dx, double dy)
    {
        double width = Width + 2 * dx;
        double height = Height + 2 * dy;
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(dx), "Deflating would produce a negative width or height.");
        return new Rectangle2D(X - dx, Y - dy, width, height);
    }

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(Rectangle2D other)
        => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) => obj is Rectangle2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>True if every corner is within <paramref name="tolerance"/> mm of the other rectangle's.</summary>
    public bool AlmostEquals(Rectangle2D other, double tolerance = GeometryTolerance.Default)
        => TopLeft.AlmostEquals(other.TopLeft, tolerance) && BottomRight.AlmostEquals(other.BottomRight, tolerance);

    public static bool operator ==(Rectangle2D left, Rectangle2D right) => left.Equals(right);
    public static bool operator !=(Rectangle2D left, Rectangle2D right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"Rect({X:0.###}, {Y:0.###}, {Width:0.###}×{Height:0.###})");
}
