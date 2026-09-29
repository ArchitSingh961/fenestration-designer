using System.Globalization;
using System.Windows;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Renders the project's frames straight from the domain model:
/// glass (with its derived size) → divisions (face to face) → outer frame → selection → dimensions.
/// It holds no geometry of its own; every draw reads the current <see cref="Project"/>, so the model is the
/// only source of truth. Frame children are frame-relative and are offset by the frame's world position here.
/// </summary>
public sealed class ProjectLayer : IViewportLayer
{
    /// <summary>Glass labels are only drawn if the glass is at least this big on screen (px).</summary>
    private const double MinGlassLabelWidthPixels = 70.0;
    private const double MinGlassLabelHeightPixels = 24.0;

    /// <summary>Screen margin (px) kept around a frame when culling, so its dimensions aren't clipped.</summary>
    private const double CullMarginPixels = 60.0;

    private readonly Func<Project> _project;
    private readonly Func<Guid, bool> _isSelected;
    private readonly DimensionRenderer _dimensions = new();

    public ProjectLayer(Func<Project> project, Func<Guid, bool> isSelected)
    {
        _project = project;
        _isSelected = isSelected;
    }

    public BoundingBox2D Bounds => BoundingBox2D.FromRectangles(_project().Frames.Select(f => f.Bounds));

    public void Render(ViewportDrawingContext context)
    {
        double cullMarginMm = context.Transform.ScreenToWorldDistance(CullMarginPixels);
        foreach (var frame in _project().Frames)
        {
            if (!context.IsVisible(frame.Bounds.Bounds.Expand(cullMarginMm))) continue;
            RenderFrame(context, frame);
        }
    }

    private void RenderFrame(ViewportDrawingContext context, Frame frame)
    {
        var origin = new Vector2D(frame.X, frame.Y);

        foreach (var glass in frame.GlassPanels)
            RenderGlass(context, glass, origin);

        foreach (var profile in frame.Profiles.Where(p => p.ProfileType != ProfileType.Frame))
        {
            bool selected = _isSelected(profile.Id);
            var body = FrameLayout.GetMemberBody(frame, profile).Offset(origin);
            context.DrawRectangle(selected ? DesignTheme.SelectedProfileFill : DesignTheme.ProfileFill,
                selected ? DesignTheme.SelectionOutline : DesignTheme.ProfileOutline, body);
        }

        RenderOuterFrame(context, frame, origin);
        _dimensions.Render(context, frame);
    }

    private void RenderGlass(ViewportDrawingContext context, GlassPanel glass, Vector2D origin)
    {
        bool selected = _isSelected(glass.Id);
        var rect = glass.Boundary.Offset(origin);
        context.DrawRectangle(selected ? DesignTheme.SelectedGlassFill : DesignTheme.GlassFill,
            selected ? DesignTheme.SelectionOutline : DesignTheme.GlassOutline, rect);

        double widthPx = context.Transform.WorldToScreenDistance(rect.Width);
        double heightPx = context.Transform.WorldToScreenDistance(rect.Height);
        if (widthPx < MinGlassLabelWidthPixels || heightPx < MinGlassLabelHeightPixels) return;

        string label = string.Create(CultureInfo.InvariantCulture, $"{glass.Boundary.Width:0.#} × {glass.Boundary.Height:0.#}");
        var text = context.CreateText(label, DesignTheme.GlassLabel, DesignTheme.GlassLabelFontSize);
        context.DrawTextCentered(text, context.ToScreen(rect.Center));
    }

    /// <summary>
    /// The four outer members share one fill; outlines are drawn only on the outer edge and the inner
    /// opening, so the corners read as one continuous frame.
    /// </summary>
    private void RenderOuterFrame(ViewportDrawingContext context, Frame frame, Vector2D origin)
    {
        bool selected = _isSelected(frame.Id);
        foreach (var member in frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame))
            context.DrawRectangle(DesignTheme.ProfileFill, null, member.GetBounds().Offset(origin));

        var outline = selected ? DesignTheme.SelectionOutline : DesignTheme.ProfileOutline;
        context.DrawRectangle(null, outline, frame.Bounds);
        if (FrameMembers.Find(frame) is { } outer)
            context.DrawRectangle(null, outline, outer.InnerOpening.Offset(origin));
    }
}
