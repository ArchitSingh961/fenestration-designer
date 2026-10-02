using Mark.Core.Geometry;
using Mark.Core.Utilities;

namespace Mark.Core.Models;

/// <summary>
/// A linear structural profile within a frame (mullion, transom, sash, etc.).
/// All coordinates are in millimetres, relative to the parent frame's origin.
/// </summary>
public class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The functional role of this profile.</summary>
    public ProfileType ProfileType { get; set; } = ProfileType.Generic;

    /// <summary>Start point relative to the parent frame, in mm.</summary>
    public Point2D StartPoint { get; set; }

    /// <summary>End point relative to the parent frame, in mm.</summary>
    public Point2D EndPoint { get; set; }

    /// <summary>Cross-section thickness in mm.</summary>
    public double Thickness { get; set; } = Units.DefaultProfileThicknessMm;

    /// <summary>Rotation angle in degrees (0 = natural orientation along the line).</summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Stable Id of the <see cref="Library.ProfileDefinition"/> this member is made from (the product: system,
    /// weight, cost, cutting data). Null means "the library's default for this role". The design never copies
    /// product data; it only references it, so a profile can be swapped after design without recreating it.
    /// </summary>
    public string? ProfileDefinitionId { get; set; }

    /// <summary>
    /// Extensible key-value metadata.
    /// The calculation engine or user may store additional data here.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    // ── Derived geometry ────────────────────────────────────────────

    public LineSegment2D Segment => new(StartPoint, EndPoint);

    public double Length => StartPoint.DistanceTo(EndPoint);

    /// <summary>
    /// Angle of the profile in degrees [0, 360), measured clockwise (on screen) from the positive X axis.
    /// 0° = horizontal →, 90° = vertical ↓.
    /// </summary>
    public double Angle => AngleMath.DirectionAngle(EndPoint - StartPoint);

    /// <summary>
    /// Creates a profile with validated geometry: finite endpoints, non-zero length and a valid thickness.
    /// </summary>
    /// <exception cref="ArgumentException">The geometry is invalid.</exception>
    public static Profile Create(ProfileType type, Point2D start, Point2D end,
        double thickness = Units.DefaultProfileThicknessMm)
    {
        GeometryValidation.EnsureNonDegenerate(new LineSegment2D(start, end), paramName: nameof(end));
        ValidationHelper.EnsureValidThickness(thickness, nameof(thickness));
        return new Profile { ProfileType = type, StartPoint = start, EndPoint = end, Thickness = thickness };
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of this profile, accounting for thickness.
    /// </summary>
    public Rectangle2D GetBounds()
    {
        double halfThick = Thickness / 2.0;
        double minX = Math.Min(StartPoint.X, EndPoint.X) - halfThick;
        double minY = Math.Min(StartPoint.Y, EndPoint.Y) - halfThick;
        double maxX = Math.Max(StartPoint.X, EndPoint.X) + halfThick;
        double maxY = Math.Max(StartPoint.Y, EndPoint.Y) + halfThick;
        return new Rectangle2D(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Creates a deep copy of this profile with a new Id.</summary>
    public Profile Clone()
    {
        return new Profile
        {
            Id = Guid.NewGuid(),
            ProfileType = ProfileType,
            StartPoint = StartPoint,
            EndPoint = EndPoint,
            Thickness = Thickness,
            Rotation = Rotation,
            ProfileDefinitionId = ProfileDefinitionId,
            Properties = new Dictionary<string, string>(Properties)
        };
    }
}
