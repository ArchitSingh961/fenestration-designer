namespace Mark.Core.Geometry;

/// <summary>
/// Result of projecting a point onto a line or segment.
/// </summary>
/// <param name="Point">The foot of the perpendicular (closest point).</param>
/// <param name="T">Parameter of <paramref name="Point"/> along the line: 0 → Start, 1 → End.
/// For an infinite line it may lie outside [0, 1]; for a segment it is clamped to [0, 1].</param>
/// <param name="Distance">Distance in mm from the original point to <paramref name="Point"/>.</param>
public readonly record struct ProjectionResult(Point2D Point, double T, double Distance);

/// <summary>
/// Perpendicular projection of points onto lines and segments — the core query behind
/// "closest point on a profile" hit testing and edge/centre-line snapping.
/// </summary>
public static class Projection2D
{
    /// <summary>
    /// Projects <paramref name="point"/> onto the INFINITE line through <paramref name="line"/>.
    /// If the line is degenerate (no direction), its start point is returned with T = 0.
    /// </summary>
    public static ProjectionResult ProjectPointOntoLine(Point2D point, LineSegment2D line)
        => Project(point, line, clampToSegment: false);

    /// <summary>
    /// Projects <paramref name="point"/> onto the segment, clamping to its endpoints.
    /// </summary>
    public static ProjectionResult ProjectPointOntoSegment(Point2D point, LineSegment2D segment)
        => Project(point, segment, clampToSegment: true);

    private static ProjectionResult Project(Point2D point, LineSegment2D line, bool clampToSegment)
    {
        Vector2D d = line.Delta;
        double lengthSquared = d.LengthSquared;
        if (lengthSquared <= GeometryTolerance.Epsilon * GeometryTolerance.Epsilon)
            return new ProjectionResult(line.Start, 0.0, point.DistanceTo(line.Start));

        // Scalar projection of (point − Start) onto d, expressed as a fraction of d's length.
        double t = (point - line.Start).Dot(d) / lengthSquared;
        if (clampToSegment)
            t = Math.Clamp(t, 0.0, 1.0);

        Point2D foot = line.PointAt(t);
        return new ProjectionResult(foot, t, point.DistanceTo(foot));
    }
}
