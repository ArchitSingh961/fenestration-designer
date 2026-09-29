namespace Fenestration.Core.Geometry;

/// <summary>How two segments meet.</summary>
public enum SegmentIntersectionKind
{
    /// <summary>The segments do not meet.</summary>
    None,

    /// <summary>The segments meet at a single point (crossing, T-junction or shared endpoint).</summary>
    Point,

    /// <summary>The segments are collinear and share a stretch of non-zero length.</summary>
    Overlap
}

/// <summary>
/// Result of <see cref="Intersection2D.IntersectSegments"/>.
/// For <see cref="SegmentIntersectionKind.Point"/>: <see cref="Point"/> is the meeting point.
/// For <see cref="SegmentIntersectionKind.Overlap"/>: <see cref="Overlap"/> is the shared stretch
/// (oriented like segment A) and <see cref="Point"/> is its start.
/// <see cref="ParameterA"/>/<see cref="ParameterB"/> locate <see cref="Point"/> on each segment (0..1).
/// </summary>
public readonly record struct SegmentIntersection(
    SegmentIntersectionKind Kind,
    Point2D Point,
    LineSegment2D Overlap,
    double ParameterA,
    double ParameterB)
{
    public static SegmentIntersection None => default;

    public bool Intersects => Kind != SegmentIntersectionKind.None;

    internal static SegmentIntersection AtPoint(Point2D point, double tA, double tB)
        => new(SegmentIntersectionKind.Point, point, new LineSegment2D(point, point), tA, tB);
}

/// <summary>
/// Intersection of two INFINITE lines. Parameters locate <see cref="Point"/> relative to each
/// defining segment and may be outside [0, 1].
/// </summary>
public readonly record struct LineIntersection(Point2D Point, double ParameterA, double ParameterB);

/// <summary>
/// Intersection queries between segments, infinite lines and rectangles.
/// All tolerances are distances in mm (default <see cref="GeometryTolerance.Default"/>).
/// </summary>
public static class Intersection2D
{
    // ── Infinite lines ──────────────────────────────────────────────

    /// <summary>
    /// Intersects the infinite lines through <paramref name="lineA"/> and <paramref name="lineB"/>.
    /// Returns null when the lines are parallel or coincident (no unique point).
    /// </summary>
    /// <exception cref="ArgumentException">A defining segment is degenerate (a line needs a direction).</exception>
    public static LineIntersection? IntersectLines(
        LineSegment2D lineA, LineSegment2D lineB, double tolerance = GeometryTolerance.Default)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);
        GeometryValidation.EnsureNonDegenerate(lineA, GeometryTolerance.Epsilon);
        GeometryValidation.EnsureNonDegenerate(lineB, GeometryTolerance.Epsilon);

        Vector2D r = lineA.Delta, s = lineB.Delta;
        if (AreParallel(r, s, tolerance))
            return null;

        (double t, double u) = SolveParameters(lineA.Start, r, lineB.Start, s);
        return new LineIntersection(lineA.PointAt(t), t, u);
    }

    // ── Segments ────────────────────────────────────────────────────

    /// <summary>
    /// Intersects two finite segments. Endpoints within <paramref name="tolerance"/> of the other segment
    /// count as touching; collinear segments sharing more than <paramref name="tolerance"/> are an overlap.
    /// </summary>
    public static SegmentIntersection IntersectSegments(
        LineSegment2D a, LineSegment2D b, double tolerance = GeometryTolerance.Default)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);

        bool aIsPoint = a.IsDegenerate(tolerance);
        bool bIsPoint = b.IsDegenerate(tolerance);
        if (aIsPoint && bIsPoint)
            return a.Start.AlmostEquals(b.Start, tolerance) ? SegmentIntersection.AtPoint(a.Start, 0, 0) : SegmentIntersection.None;
        if (aIsPoint)
            return PointOnSegment(a.Start, b, tolerance, pointIsA: true);
        if (bIsPoint)
            return PointOnSegment(b.Start, a, tolerance, pointIsA: false);

        Vector2D r = a.Delta, s = b.Delta;
        if (AreParallel(r, s, tolerance))
            return IntersectParallel(a, b, tolerance);

        (double t, double u) = SolveParameters(a.Start, r, b.Start, s);

        // Accept near-misses at the ends: convert the mm tolerance into each segment's parameter space.
        double tolA = tolerance / r.Length;
        double tolB = tolerance / s.Length;
        if (t < -tolA || t > 1 + tolA || u < -tolB || u > 1 + tolB)
            return SegmentIntersection.None;

        t = Math.Clamp(t, 0.0, 1.0);
        u = Math.Clamp(u, 0.0, 1.0);
        return SegmentIntersection.AtPoint(a.PointAt(t), t, u);
    }

    // ── Segment × rectangle ─────────────────────────────────────────

    /// <summary>
    /// Points where <paramref name="segment"/> crosses or touches the rectangle's BOUNDARY,
    /// ordered from the segment's start. Where the segment runs along an edge, both ends of
    /// the shared stretch are returned. Points closer than <paramref name="tolerance"/> are merged.
    /// </summary>
    public static IReadOnlyList<Point2D> IntersectSegmentWithRectangle(
        LineSegment2D segment, Rectangle2D rect, double tolerance = GeometryTolerance.Default)
    {
        GeometryValidation.EnsureValidTolerance(tolerance);

        var hits = new List<(double T, Point2D Point)>();
        foreach (var edge in rect.GetEdges())
        {
            var hit = IntersectSegments(segment, edge, tolerance);
            if (hit.Kind == SegmentIntersectionKind.Point)
                hits.Add((hit.ParameterA, hit.Point));
            else if (hit.Kind == SegmentIntersectionKind.Overlap)
            {
                hits.Add((Projection2D.ProjectPointOntoSegment(hit.Overlap.Start, segment).T, hit.Overlap.Start));
                hits.Add((Projection2D.ProjectPointOntoSegment(hit.Overlap.End, segment).T, hit.Overlap.End));
            }
        }

        var result = new List<Point2D>();
        foreach (var (_, point) in hits.OrderBy(h => h.T))
        {
            // Corners are found by two edges; keep only one copy.
            if (result.Count == 0 || !result[^1].AlmostEquals(point, tolerance))
                result.Add(point);
        }
        return result;
    }

    /// <summary>
    /// The part of <paramref name="segment"/> that lies inside the (closed) rectangle, or null if none.
    /// Uses the Liang–Barsky parametric clipping algorithm.
    /// </summary>
    public static LineSegment2D? ClipSegmentToRectangle(LineSegment2D segment, Rectangle2D rect)
    {
        double dx = segment.End.X - segment.Start.X;
        double dy = segment.End.Y - segment.Start.Y;
        double tEnter = 0.0, tExit = 1.0;

        // Each pair (p, q) describes one boundary: the segment is inside that boundary where p·t ≤ q.
        ReadOnlySpan<double> p = stackalloc double[] { -dx, dx, -dy, dy };
        ReadOnlySpan<double> q = stackalloc double[]
        {
            segment.Start.X - rect.Left,
            rect.Right - segment.Start.X,
            segment.Start.Y - rect.Top,
            rect.Bottom - segment.Start.Y
        };

        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                // Parallel to this boundary: either wholly outside it, or it imposes no limit.
                if (q[i] < 0) return null;
                continue;
            }

            double t = q[i] / p[i];
            if (p[i] < 0)
            {
                if (t > tExit) return null;
                tEnter = Math.Max(tEnter, t);
            }
            else
            {
                if (t < tEnter) return null;
                tExit = Math.Min(tExit, t);
            }
        }

        return new LineSegment2D(segment.PointAt(tEnter), segment.PointAt(tExit));
    }

    // ── Internals ───────────────────────────────────────────────────

    /// <summary>
    /// Two directions are treated as parallel when, over the length of the longer one, they drift apart
    /// by no more than <paramref name="tolerance"/> mm. This keeps the test in real-world units instead of
    /// an arbitrary angle, so a 3 m transom and a 300 mm bead are judged consistently.
    /// </summary>
    private static bool AreParallel(Vector2D r, Vector2D s, double tolerance)
    {
        double lengthA = r.Length, lengthB = s.Length;
        double sine = Math.Abs(r.Cross(s)) / (lengthA * lengthB);
        return sine * Math.Max(lengthA, lengthB) <= Math.Max(tolerance, GeometryTolerance.Epsilon);
    }

    /// <summary>
    /// Solves pA + t·r = pB + u·s for (t, u). Caller guarantees r and s are not parallel.
    /// Crossing both sides with s (resp. r) eliminates the other unknown.
    /// </summary>
    private static (double T, double U) SolveParameters(Point2D pA, Vector2D r, Point2D pB, Vector2D s)
    {
        Vector2D qp = pB - pA;
        double denominator = r.Cross(s);
        return (qp.Cross(s) / denominator, qp.Cross(r) / denominator);
    }

    private static SegmentIntersection PointOnSegment(Point2D point, LineSegment2D segment, double tolerance, bool pointIsA)
    {
        var projection = Projection2D.ProjectPointOntoSegment(point, segment);
        if (projection.Distance > tolerance)
            return SegmentIntersection.None;
        return pointIsA
            ? SegmentIntersection.AtPoint(point, 0, projection.T)
            : SegmentIntersection.AtPoint(point, projection.T, 0);
    }

    private static SegmentIntersection IntersectParallel(LineSegment2D a, LineSegment2D b, double tolerance)
    {
        // Parallel but offset → no contact.
        if (Projection2D.ProjectPointOntoLine(b.Start, a).Distance > tolerance
            || Projection2D.ProjectPointOntoLine(b.End, a).Distance > tolerance)
            return SegmentIntersection.None;

        // Collinear: express B's endpoints in A's parameter space and intersect the intervals.
        double t0 = Projection2D.ProjectPointOntoLine(b.Start, a).T;
        double t1 = Projection2D.ProjectPointOntoLine(b.End, a).T;
        double low = Math.Max(0.0, Math.Min(t0, t1));
        double high = Math.Min(1.0, Math.Max(t0, t1));

        double tolA = tolerance / a.Length;
        if (low > high + tolA)
            return SegmentIntersection.None; // gap along the line is wider than the tolerance

        Point2D start = a.PointAt(Math.Min(low, 1.0));
        Point2D end = a.PointAt(Math.Max(high, 0.0));

        if (start.DistanceTo(end) <= tolerance || low >= high)
        {
            // Touching end-to-end (or a sub-tolerance overlap): a single meeting point.
            double t = Math.Clamp((low + high) / 2.0, 0.0, 1.0);
            Point2D touch = a.PointAt(t);
            return SegmentIntersection.AtPoint(touch, t, Projection2D.ProjectPointOntoSegment(touch, b).T);
        }

        return new SegmentIntersection(
            SegmentIntersectionKind.Overlap,
            start,
            new LineSegment2D(start, end),
            low,
            Projection2D.ProjectPointOntoSegment(start, b).T);
    }
}
