using System.Globalization;

namespace Mark.Core.Geometry;

/// <summary>
/// Immutable axis-aligned bounding box (mm). Used for fit-to-screen, selection boxes and hit-test culling.
///
/// A box is either EMPTY (encloses nothing — <c>default</c> and <see cref="Empty"/>) or has
/// MinX ≤ MaxX and MinY ≤ MaxY. Reading extents of an empty box throws, so a missing
/// bound can never silently become (0, 0). Every "expand" operation returns a new box.
/// </summary>
public readonly struct BoundingBox2D : IEquatable<BoundingBox2D>
{
    private readonly bool _hasValue;
    private readonly double _minX, _minY, _maxX, _maxY;

    private BoundingBox2D(double minX, double minY, double maxX, double maxY)
    {
        _hasValue = true;
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
    }

    /// <summary>A box that encloses nothing. Expanding it by anything yields that thing's bounds.</summary>
    public static BoundingBox2D Empty => default;

    public bool IsEmpty => !_hasValue;

    public double MinX => _hasValue ? _minX : throw EmptyError();
    public double MinY => _hasValue ? _minY : throw EmptyError();
    public double MaxX => _hasValue ? _maxX : throw EmptyError();
    public double MaxY => _hasValue ? _maxY : throw EmptyError();

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Point2D Center => new((MinX + MaxX) / 2.0, (MinY + MaxY) / 2.0);

    public Rectangle2D ToRectangle() => new(MinX, MinY, Width, Height);

    // ── Factories ───────────────────────────────────────────────────

    /// <exception cref="ArgumentException">Values are not finite or min exceeds max.</exception>
    public static BoundingBox2D FromMinMax(double minX, double minY, double maxX, double maxY)
    {
        GeometryValidation.EnsureFinite(minX);
        GeometryValidation.EnsureFinite(minY);
        GeometryValidation.EnsureFinite(maxX);
        GeometryValidation.EnsureFinite(maxY);
        if (minX > maxX || minY > maxY)
            throw new ArgumentException("Minimum must not exceed maximum.");
        return new BoundingBox2D(minX, minY, maxX, maxY);
    }

    public static BoundingBox2D FromPoints(params Point2D[] points) => FromPoints((IEnumerable<Point2D>)points);

    public static BoundingBox2D FromPoints(IEnumerable<Point2D> points)
    {
        var box = Empty;
        foreach (var p in points)
            box = box.Expand(p);
        return box;
    }

    public static BoundingBox2D FromSegment(LineSegment2D segment)
        => Empty.Expand(segment.Start).Expand(segment.End);

    public static BoundingBox2D FromSegments(IEnumerable<LineSegment2D> segments)
        => Union(segments.Select(FromSegment));

    public static BoundingBox2D FromRectangle(Rectangle2D rect)
        => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static BoundingBox2D FromRectangles(IEnumerable<Rectangle2D> rects)
        => Union(rects.Select(FromRectangle));

    /// <summary>Union of any number of boxes (empty boxes are ignored).</summary>
    public static BoundingBox2D Union(IEnumerable<BoundingBox2D> boxes)
    {
        var result = Empty;
        foreach (var b in boxes)
            result = result.Union(b);
        return result;
    }

    // ── Expansion ───────────────────────────────────────────────────

    /// <summary>Smallest box enclosing this box and <paramref name="point"/>.</summary>
    public BoundingBox2D Expand(Point2D point)
    {
        GeometryValidation.EnsureFinite(point);
        if (!_hasValue)
            return new BoundingBox2D(point.X, point.Y, point.X, point.Y);
        return new BoundingBox2D(
            Math.Min(_minX, point.X), Math.Min(_minY, point.Y),
            Math.Max(_maxX, point.X), Math.Max(_maxY, point.Y));
    }

    /// <summary>
    /// Grows every side by <paramref name="margin"/> mm (negative shrinks). An empty box stays empty.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Shrinking would invert the box.</exception>
    public BoundingBox2D Expand(double margin)
    {
        GeometryValidation.EnsureFinite(margin);
        if (!_hasValue)
            return this;
        if (Width + 2 * margin < 0 || Height + 2 * margin < 0)
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "Shrinking would invert the bounding box.");
        return new BoundingBox2D(_minX - margin, _minY - margin, _maxX + margin, _maxY + margin);
    }

    /// <summary>Smallest box enclosing both boxes.</summary>
    public BoundingBox2D Union(BoundingBox2D other)
    {
        if (!other._hasValue) return this;
        if (!_hasValue) return other;
        return new BoundingBox2D(
            Math.Min(_minX, other._minX), Math.Min(_minY, other._minY),
            Math.Max(_maxX, other._maxX), Math.Max(_maxY, other._maxY));
    }

    // ── Queries (closed intervals; empty boxes contain/intersect nothing) ──

    public bool Contains(Point2D point, double tolerance = 0.0)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);
        return _hasValue
            && point.X >= _minX - tolerance && point.X <= _maxX + tolerance
            && point.Y >= _minY - tolerance && point.Y <= _maxY + tolerance;
    }

    /// <summary>True if <paramref name="other"/> lies entirely inside this box.</summary>
    public bool Contains(BoundingBox2D other)
        => _hasValue && other._hasValue
        && other._minX >= _minX && other._maxX <= _maxX
        && other._minY >= _minY && other._maxY <= _maxY;

    /// <summary>True if the boxes overlap or touch.</summary>
    public bool Intersects(BoundingBox2D other)
        => _hasValue && other._hasValue
        && _minX <= other._maxX && _maxX >= other._minX
        && _minY <= other._maxY && _maxY >= other._minY;

    // ── Equality ────────────────────────────────────────────────────

    public bool Equals(BoundingBox2D other)
        => _hasValue == other._hasValue
        && (!_hasValue || (_minX.Equals(other._minX) && _minY.Equals(other._minY)
                           && _maxX.Equals(other._maxX) && _maxY.Equals(other._maxY)));

    public override bool Equals(object? obj) => obj is BoundingBox2D other && Equals(other);
    public override int GetHashCode() => _hasValue ? HashCode.Combine(_minX, _minY, _maxX, _maxY) : 0;

    public static bool operator ==(BoundingBox2D left, BoundingBox2D right) => left.Equals(right);
    public static bool operator !=(BoundingBox2D left, BoundingBox2D right) => !left.Equals(right);

    public override string ToString()
        => _hasValue
            ? string.Create(CultureInfo.InvariantCulture, $"Box({_minX:0.###}, {_minY:0.###} → {_maxX:0.###}, {_maxY:0.###})")
            : "Box(empty)";

    private static InvalidOperationException EmptyError()
        => new("The bounding box is empty; check IsEmpty before reading its extents.");
}
