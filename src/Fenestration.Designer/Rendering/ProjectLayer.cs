using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Renders the project's frames straight from the domain model through <see cref="FrameRenderer"/>.
/// It holds no geometry of its own; every draw reads the current <see cref="Project"/>, so the model is the
/// only source of truth. In the Outside view each frame is drawn from a mirrored copy
/// (<see cref="OpeningGeometry.Mirror"/>); the model itself is never changed for display.
/// </summary>
public sealed class ProjectLayer : IViewportLayer
{
    /// <summary>Screen margin (px) kept around a frame when culling, so its dimensions aren't clipped.</summary>
    private const double CullMarginPixels = 80.0;

    private readonly Func<Project> _project;
    private readonly Func<Guid, bool> _isSelected;
    private readonly Func<Guid, Frame?> _previewFor;
    private readonly Func<bool> _isOutsideView;
    private readonly FrameRenderer _renderer;

    /// <param name="previewFor">
    /// Optional: returns a candidate (preview) copy of a frame while an interaction is in progress. It is drawn
    /// in place of the committed frame, so the user sees the result before anything is committed.
    /// </param>
    /// <param name="isOutsideView">Optional: true to draw the design as seen from outside.</param>
    /// <param name="rules">Design rules for sash geometry (defaults if null).</param>
    public ProjectLayer(Func<Project> project, Func<Guid, bool> isSelected, Func<Guid, Frame?>? previewFor = null,
        Func<bool>? isOutsideView = null, DesignRules? rules = null)
    {
        _project = project;
        _isSelected = isSelected;
        _previewFor = previewFor ?? (_ => null);
        _isOutsideView = isOutsideView ?? (() => false);
        _renderer = new FrameRenderer(rules ?? new DesignRules());
    }

    public BoundingBox2D Bounds => BoundingBox2D.FromRectangles(_project().Frames.Select(FrameRenderer.DrawnBounds));

    public void Render(ViewportDrawingContext context)
    {
        double cullMarginMm = context.Transform.ScreenToWorldDistance(CullMarginPixels);
        bool outside = _isOutsideView();
        var frames = _project().Frames;
        for (int i = 0; i < frames.Count; i++)
        {
            var frame = _previewFor(frames[i].Id) ?? frames[i];
            if (!context.IsVisible(FrameRenderer.DrawnBounds(frame).Bounds.Expand(cullMarginMm))) continue;
            _renderer.Render(context, outside ? OpeningGeometry.Mirror(frame) : frame, i + 1, _isSelected,
                FrameRenderOptions.Drawing);
        }
    }
}
