using System.Windows;
using System.Windows.Media;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Designer.Rendering;

/// <summary>
/// Small pictures of designs, drawn with the same <see cref="FrameRenderer"/> as the drawing view, so a library
/// icon always shows exactly what applying it gives. Vector images (<see cref="DrawingImage"/>), frozen, cached
/// per template.
/// </summary>
public static class DesignThumbnails
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    /// <summary>The picture of a library design on a frame of its suggested size; null if it cannot be built.</summary>
    public static ImageSource? For(DesignTemplate template, DesignRules rules, double maxWidth = 56, double maxHeight = 56)
    {
        ArgumentNullException.ThrowIfNull(template);
        string key = $"{template.Id}|{maxWidth}|{maxHeight}|{rules.FrameThicknessMm}|{rules.MullionThicknessMm}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var (width, height) = template.SuggestedSize;
        if (!FrameEditor.TryCreateFrame(0, 0, width, height, rules, out var frame).Success) return null;
        // Mesh-only designs are shown on a casement, so there is a sash to show the mesh on.
        if (template.KeepsLayout)
            FrameEditor.TrySetOpening(frame!, frame!.GlassPanels.Select(g => g.Id).ToList(), OpeningType.SideHungLeft, null, rules);
        if (!FrameEditor.TryApplyTemplate(frame!, template, null, rules).Success) return null;

        var image = Render(frame!, rules, maxWidth, maxHeight);
        Cache[key] = image;
        return image;
    }

    /// <summary>A picture of <paramref name="frame"/> fitted into <paramref name="maxWidth"/> × <paramref name="maxHeight"/> DIPs.</summary>
    /// <param name="glassLook">How each glass type is drawn (null: all glass clear).</param>
    public static ImageSource Render(Frame frame, DesignRules rules, double maxWidth, double maxHeight,
        FrameRenderOptions? options = null, Func<string?, Mark.Core.Library.GlassLook?>? glassLook = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double zoom = Math.Clamp(Math.Min(maxWidth / frame.Width, maxHeight / frame.Height),
            ViewportTransform.MinZoom, ViewportTransform.MaxZoom);
        var transform = new ViewportTransform { Zoom = zoom, PanX = frame.X, PanY = frame.Y };

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var size = new Size(frame.Width * zoom, frame.Height * zoom);
            // A transparent backdrop fixes the image size, so all thumbnails of one size line up.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, maxWidth, maxHeight));
            dc.PushTransform(new TranslateTransform((maxWidth - size.Width) / 2, (maxHeight - size.Height) / 2));
            var context = new ViewportDrawingContext(dc, transform, size, 1.0);
            new FrameRenderer(rules, glassLook).Render(context, frame, 1, _ => false, options ?? FrameRenderOptions.Thumbnail);
            dc.Pop();
        }
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
