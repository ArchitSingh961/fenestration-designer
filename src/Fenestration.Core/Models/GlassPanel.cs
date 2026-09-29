using Fenestration.Core.Geometry;
using Fenestration.Core.Utilities;

namespace Fenestration.Core.Models;

/// <summary>
/// A glass panel occupying a region within a frame.
/// Coordinates are in millimetres, relative to the parent frame's origin.
/// </summary>
public class GlassPanel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The rectangular boundary of the glass panel in mm.
    /// Manufacturing deductions will be computed by the calculation engine.
    /// </summary>
    public Rectangle2D Boundary { get; set; }

    /// <summary>Glass thickness in mm (e.g. 4, 6, 8, 24 for IGU).</summary>
    public double Thickness { get; set; } = Units.DefaultGlassThicknessMm;

    /// <summary>Extensible metadata for future calculation-engine use.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>Creates a deep copy of this panel with a new Id.</summary>
    public GlassPanel Clone()
    {
        return new GlassPanel
        {
            Id = Guid.NewGuid(),
            Boundary = Boundary,
            Thickness = Thickness,
            Properties = new Dictionary<string, string>(Properties)
        };
    }
}
