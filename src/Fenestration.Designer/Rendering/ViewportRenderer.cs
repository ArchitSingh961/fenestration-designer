using System.Windows;
using Fenestration.Core.Viewport;

namespace Fenestration.Designer.Rendering;

/// <summary>What the background pass should draw.</summary>
public readonly record struct ViewportRenderOptions(bool ShowGrid, bool ShowAxes, ViewportSettings Settings);

/// <summary>
/// Orchestrates the drawing passes. The control keeps two visuals so that content changes
/// don't force the grid to be regenerated:
/// <list type="number">
///   <item><see cref="RenderBackground"/>: background fill, grid, axes, ruler labels.</item>
///   <item><see cref="RenderContent"/>: every <see cref="IViewportLayer"/> in order, skipping off-screen layers.</item>
/// </list>
/// </summary>
public sealed class ViewportRenderer
{
    private readonly GridRenderer _grid = new();
    private readonly CoordinateRenderer _coordinates = new();

    public void RenderBackground(ViewportDrawingContext context, ViewportRenderOptions options)
    {
        var size = context.ViewportSize;
        context.DrawingContext.DrawRectangle(ViewportTheme.Background, null, new Rect(0, 0, size.Width, size.Height));

        if (size.Width <= 0 || size.Height <= 0)
            return;

        var spacing = GridSpacing.ForZoom(context.Zoom, options.Settings.MinGridPixelSpacing);

        if (options.ShowGrid)
            _grid.Render(context, spacing, options.Settings.MaxGridLinesPerAxis);

        if (options.ShowAxes)
        {
            _coordinates.RenderAxes(context);
            _coordinates.RenderRulerLabels(context, spacing, options.Settings.MaxGridLinesPerAxis);
        }
    }

    public void RenderContent(ViewportDrawingContext context, IEnumerable<IViewportLayer> layers)
    {
        foreach (var layer in layers)
        {
            if (context.IsVisible(layer.Bounds))
                layer.Render(context);
        }
    }

    /// <summary>Overlay layers draw screen feedback and decide for themselves what is visible, so they aren't culled.</summary>
    public void RenderOverlay(ViewportDrawingContext context, IEnumerable<IViewportLayer> layers)
    {
        foreach (var layer in layers)
            layer.Render(context);
    }
}
