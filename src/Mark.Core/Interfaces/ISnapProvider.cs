using Mark.Core.Geometry;

namespace Mark.Core.Interfaces;

/// <summary>
/// Result of a snap query — the snapped point and what kind of snap it was.
/// </summary>
public class SnapResult
{
    /// <summary>The snapped-to point in world mm.</summary>
    public Point2D Point { get; init; }

    /// <summary>What kind of geometry feature was snapped to.</summary>
    public SnapType Type { get; init; }

    /// <summary>Distance from the original query point to the snapped point.</summary>
    public double Distance { get; init; }
}

/// <summary>
/// The kind of geometry feature a snap locked onto.
/// </summary>
public enum SnapType
{
    None,
    Endpoint,
    Midpoint,
    Edge,
    Intersection,
    Center,
    Grid
}

/// <summary>
/// Abstraction for geometry snapping. Implementations gather snap candidates
/// from the current scene and return the closest match within tolerance.
/// </summary>
public interface ISnapProvider
{
    /// <summary>
    /// Finds the best snap target near <paramref name="worldPoint"/>.
    /// Returns null if no snap is within tolerance.
    /// </summary>
    /// <param name="worldPoint">The cursor position in world mm.</param>
    /// <param name="toleranceMm">Maximum distance to consider for snapping, in mm.</param>
    /// <param name="excludeIds">Object IDs to exclude (e.g. the object being dragged).</param>
    SnapResult? FindSnap(Point2D worldPoint, double toleranceMm, IReadOnlySet<Guid>? excludeIds = null);

    /// <summary>Gets or sets whether snapping is enabled.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Gets or sets the snap tolerance in mm.</summary>
    double ToleranceMm { get; set; }
}
