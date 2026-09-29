using Fenestration.Core.Geometry;

namespace Fenestration.Core.Models;

/// <summary>
/// A 2D linear dimension annotation between two reference points.
/// Coordinates are in millimetres, relative to the parent frame's origin.
/// The displayed value is always derived from the geometry — it cannot be overridden.
/// </summary>
public class Dimension
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>First reference point in mm.</summary>
    public Point2D StartPoint { get; set; }

    /// <summary>Second reference point in mm.</summary>
    public Point2D EndPoint { get; set; }

    /// <summary>Which axis the dimension measures along.</summary>
    public DimensionOrientation Orientation { get; set; }

    /// <summary>
    /// The measured distance in mm, projected onto the dimension's axis
    /// (horizontal → |ΔX|, vertical → |ΔY|), as in standard CAD linear dimensions.
    /// Always derived from geometry; never persisted.
    /// </summary>
    public double Value => Orientation switch
    {
        DimensionOrientation.Horizontal => Math.Abs(EndPoint.X - StartPoint.X),
        DimensionOrientation.Vertical => Math.Abs(EndPoint.Y - StartPoint.Y),
        _ => StartPoint.DistanceTo(EndPoint)
    };
}
