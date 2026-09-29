using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Core.Interfaces;

/// <summary>
/// Abstraction for rendering the design to a drawing surface.
/// Implementations are WPF-specific; the interface lives in Core so that
/// the rendering contract is visible to tests and future alternative renderers.
/// </summary>
public interface IRenderer
{
    /// <summary>Clears the entire drawing surface.</summary>
    void Clear();

    /// <summary>Draws the background grid.</summary>
    void DrawGrid(Rectangle2D viewport, double gridSpacingMm);

    /// <summary>Draws a frame outline.</summary>
    void DrawFrame(Frame frame, bool isSelected);

    /// <summary>Draws a structural profile.</summary>
    void DrawProfile(Profile profile, bool isSelected);

    /// <summary>Draws a glass panel.</summary>
    void DrawGlassPanel(GlassPanel panel, bool isSelected);

    /// <summary>Draws a dimension annotation.</summary>
    void DrawDimension(Dimension dimension);

    /// <summary>Draws a snap indicator at the given point.</summary>
    void DrawSnapIndicator(Point2D point, SnapType snapType);

    /// <summary>Draws a selection bounding box.</summary>
    void DrawSelectionBox(Rectangle2D bounds);
}
