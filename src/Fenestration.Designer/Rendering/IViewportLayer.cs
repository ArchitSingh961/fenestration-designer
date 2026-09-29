using Fenestration.Core.Geometry;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// A source of world-space content drawn by the viewport above the grid and axes.
/// Milestone 4 adds a project layer that draws frames, profiles, glass and dimensions from the
/// domain model. The viewport itself needs no changes to support it.
/// </summary>
public interface IViewportLayer
{
    /// <summary>
    /// World extents (mm) of everything this layer draws. Used by Fit to Screen and to skip
    /// the layer entirely when it is off screen. <see cref="BoundingBox2D.Empty"/> if it has nothing to draw.
    /// </summary>
    BoundingBox2D Bounds { get; }

    /// <summary>Draws the layer. Implementations should skip objects for which <c>context.IsVisible</c> is false.</summary>
    void Render(ViewportDrawingContext context);
}
