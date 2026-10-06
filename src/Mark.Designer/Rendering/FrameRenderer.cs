using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.Rendering;

/// <summary>What <see cref="FrameRenderer"/> draws besides the frame itself.</summary>
public sealed record FrameRenderOptions
{
    /// <summary>Everything: dimensions, labels, sizes, floor line, design reference (the drawing view).</summary>
    public static FrameRenderOptions Drawing { get; } = new();

    /// <summary>The bare design: profiles, glass, sashes and symbols only (library thumbnails).</summary>
    public static FrameRenderOptions Thumbnail { get; } = new()
    {
        Dimensions = false, Labels = false, GlassSizes = false, FloorLine = false, Reference = false, SymbolScale = 0.45
    };

    public bool Dimensions { get; init; } = true;

    /// <summary>Opening numbers, sash/mesh tags, handle heights and the frame tag.</summary>
    public bool Labels { get; init; } = true;

    public bool GlassSizes { get; init; } = true;

    public bool FloorLine { get; init; } = true;

    /// <summary>The design reference and quantity above the frame.</summary>
    public bool Reference { get; init; } = true;

    /// <summary>Size of handles, arrows and mesh spacing relative to the drawing view (smaller for thumbnails).</summary>
    public double SymbolScale { get; init; } = 1.0;
}

/// <summary>
/// Draws one frame from the domain model, in this order: glass → divisions → sashes (band, mesh, opening symbol,
/// handle) → outer frame → selection → labels → dimensions, reference and floor line.
/// It holds no state; every value comes from the frame, <see cref="OpeningGeometry"/> and <see cref="AutoDimensions"/>.
///
/// Opening symbols follow the usual elevation convention, seen from the side being drawn: two dashed lines run from
/// the hinged edge's corners and meet in the middle of the handle edge; tilt &amp; turn shows both the turn and the
/// tilt triangle; a pivot shows a diamond and its axis; sliding panels show an arrow in the direction they open.
/// Line weights, text and handles are in screen pixels, so the drawing reads the same at every zoom.
/// </summary>
public sealed class FrameRenderer
{
    /// <summary>Opening labels are only drawn if the opening is at least this big on screen (px).</summary>
    private const double MinLabelWidthPixels = 46.0;
    private const double MinLabelHeightPixels = 40.0;

    /// <summary>Glass size text needs a bit more room.</summary>
    private const double MinSizeLabelWidthPixels = 70.0;
    private const double MinSizeLabelHeightPixels = 64.0;

    private readonly DimensionRenderer _dimensions = new();
    private readonly DesignRules _rules;
    private readonly Func<string?, GlassLook?> _glassLook;

    /// <param name="glassLook">How a glass type (by library id; null = the library's default glass) is drawn; null: all
    /// glass plain and clear.</param>
    public FrameRenderer(DesignRules rules, Func<string?, GlassLook?>? glassLook = null)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _glassLook = glassLook ?? (_ => null);
    }

    /// <summary>Extra world area around a frame its drawing needs (floor line, reference), for culling and Fit.</summary>
    public static Rectangle2D DrawnBounds(Frame frame)
    {
        var bounds = frame.Bounds;
        if (frame.Design.FloorDistanceMm is { } floor && double.IsFinite(floor))
        {
            double overhang = FloorOverhang(frame);
            bounds = Rectangle2D.FromCorners(new Point2D(bounds.Left - overhang, bounds.Top),
                new Point2D(bounds.Right + overhang, bounds.Bottom + floor));
        }
        return bounds;
    }

    /// <param name="frameNumber">1-based position in the project, for the "F1" tag.</param>
    public void Render(ViewportDrawingContext context, Frame frame, int frameNumber, Func<Guid, bool> isSelected,
        FrameRenderOptions options)
    {
        var origin = new Vector2D(frame.X, frame.Y);

        foreach (var glass in frame.GlassPanels)
        {
            bool selected = isSelected(glass.Id);
            context.DrawRectangle(GlassBrushes.For(_glassLook(glass.GlassDefinitionId), selected),
                DesignTheme.GlassOutline, glass.Boundary.Offset(origin));
        }

        foreach (var profile in frame.Profiles.Where(p => p.ProfileType != ProfileType.Frame))
        {
            bool selected = isSelected(profile.Id);
            var body = FrameLayout.GetMemberBody(frame, profile).Offset(origin);
            context.DrawRectangle(selected ? DesignTheme.SelectedProfileFill : DesignTheme.ProfileFill,
                selected ? DesignTheme.SelectionOutline : DesignTheme.ProfileOutline, body);
        }

        foreach (var glass in frame.GlassPanels)
            RenderOpening(context, frame, glass, origin, options.SymbolScale);

        RenderOuterFrame(context, frame, origin, isSelected(frame.Id));

        foreach (var glass in frame.GlassPanels.Where(g => isSelected(g.Id)))
            context.DrawRectangle(null, DesignTheme.SelectionOutline, glass.Boundary.Offset(origin));

        if (options.Labels)
            RenderLabels(context, frame, frameNumber, origin, options.GlassSizes);
        else if (options.GlassSizes)
            foreach (var glass in frame.GlassPanels)
                RenderGlassSize(context, glass.Boundary.Offset(origin), glass.Boundary);

        if (options.Dimensions)
            _dimensions.Render(context, frame);
        if (options.Reference)
            RenderReference(context, frame);
        if (options.FloorLine)
            RenderFloor(context, frame);
    }

    // ── Openings ────────────────────────────────────────────────────

    private void RenderOpening(ViewportDrawingContext context, Frame frame, GlassPanel glass, Vector2D origin, double scale)
    {
        if (OpeningGeometry.SashOf(frame, glass, _rules) is not { } sash)
        {
            RenderFixedGlass(context, glass.Boundary.Offset(origin), scale);
            if (glass.HasMesh)
                RenderMesh(context, glass.Boundary.Offset(origin), scale);
            return;
        }

        var outer = sash.Outer.Offset(origin);
        var inner = sash.Glass.Offset(origin);
        DrawBand(context, outer, inner);
        if (glass.HasMesh)
            RenderMesh(context, inner, scale);
        RenderSymbol(context, glass.Opening, inner, scale);
        RenderHandle(context, sash.Handle + origin, sash.Side, scale);
    }

    /// <summary>
    /// A fixed pane (glass in the frame, no sash): the drawing convention for glass — two short parallel diagonal strokes
    /// near the top-left corner — and a small FIXED label at the bottom centre, so it is never mistaken for an opening.
    /// </summary>
    private static void RenderFixedGlass(ViewportDrawingContext context, Rectangle2D world, double scale)
    {
        var r = ToRect(context, world);
        if (r.Width < 14 || r.Height < 14) return;
        var dc = context.DrawingContext;
        double side = Math.Min(r.Width, r.Height);
        double length = Math.Clamp(side * 0.16, 6, 46);
        double gap = Math.Clamp(side * 0.07, 3, 16);
        var start = new Point(r.Left + Math.Clamp(side * 0.12, 4, 40), r.Top + Math.Clamp(side * 0.12, 4, 40) + length);
        for (int i = 0; i < 2; i++)
        {
            var a = new Point(start.X + i * gap, start.Y + i * gap * 0.35);
            dc.DrawLine(DesignTheme.FixedGlassPen, a, new Point(a.X + length, a.Y - length));
        }
        if (r.Width >= 60 && r.Height >= 70)
        {
            var text = context.CreateText("FIXED", DesignTheme.FixedGlassText, Math.Max(7, DesignTheme.FixedGlassFontSize * Math.Clamp(scale, 0.6, 1.4)));
            context.DrawTextCentered(text, new Point((r.Left + r.Right) / 2, r.Bottom - Math.Max(12, r.Height * 0.1)));
        }
    }

    /// <summary>The sash band: the area between its outer edge and its glass, with both edges outlined.</summary>
    private static void DrawBand(ViewportDrawingContext context, Rectangle2D outer, Rectangle2D inner)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(ToRect(context, outer)));
        geometry.Children.Add(new RectangleGeometry(ToRect(context, inner)));
        geometry.Freeze();
        context.DrawingContext.DrawGeometry(DesignTheme.SashFill, DesignTheme.SashOutline, geometry);
    }

    private static void RenderMesh(ViewportDrawingContext context, Rectangle2D world, double scale)
    {
        var r = ToRect(context, world);
        if (r.Width < 4 || r.Height < 4) return;
        double spacing = Math.Max(3.5, 9.0 * scale);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (double d = -r.Height; d < r.Width; d += spacing)
            {
                g.BeginFigure(new Point(r.Left + d, r.Top), false, false);
                g.LineTo(new Point(r.Left + d + r.Height, r.Bottom), true, false);
            }
            for (double d = 0; d < r.Width + r.Height; d += spacing)
            {
                g.BeginFigure(new Point(r.Left + d, r.Top), false, false);
                g.LineTo(new Point(r.Left + d - r.Height, r.Bottom), true, false);
            }
        }
        geometry.Freeze();

        var dc = context.DrawingContext;
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawGeometry(null, DesignTheme.MeshPen, geometry);
        dc.Pop();
    }

    private static void RenderSymbol(ViewportDrawingContext context, OpeningType type, Rectangle2D world, double scale)
    {
        var r = ToRect(context, world);
        if (r.Width < 6 || r.Height < 6) return;
        var dc = context.DrawingContext;
        var pen = DesignTheme.OpeningSymbolPen;
        Point tl = r.TopLeft, tr = r.TopRight, bl = r.BottomLeft, br = r.BottomRight;
        Point top = new((r.Left + r.Right) / 2, r.Top), bottom = new((r.Left + r.Right) / 2, r.Bottom);
        Point left = new(r.Left, (r.Top + r.Bottom) / 2), right = new(r.Right, (r.Top + r.Bottom) / 2);

        void Lines(Point a, Point b, Point apex)
        {
            dc.DrawLine(pen, a, apex);
            dc.DrawLine(pen, b, apex);
        }

        switch (type)
        {
            case OpeningType.SideHungLeft: Lines(tl, bl, right); break;
            case OpeningType.SideHungRight: Lines(tr, br, left); break;
            case OpeningType.TopHung: Lines(tl, tr, bottom); break;
            case OpeningType.BottomHung: Lines(bl, br, top); break;
            case OpeningType.TiltTurnLeft: Lines(tl, bl, right); Lines(bl, br, top); break;
            case OpeningType.TiltTurnRight: Lines(tr, br, left); Lines(bl, br, top); break;
            case OpeningType.PivotVertical:
            case OpeningType.PivotHorizontal:
                dc.DrawLine(pen, top, right);
                dc.DrawLine(pen, right, bottom);
                dc.DrawLine(pen, bottom, left);
                dc.DrawLine(pen, left, top);
                if (type == OpeningType.PivotVertical) dc.DrawLine(DesignTheme.PivotAxisPen, top, bottom);
                else dc.DrawLine(DesignTheme.PivotAxisPen, left, right);
                break;
            case OpeningType.SlidingLeft:
            case OpeningType.SlidingRight:
            case OpeningType.SlidingUp:
            case OpeningType.SlidingDown:
                RenderArrow(context, type, r, scale);
                break;
        }
    }

    /// <summary>The direction arrow of a sliding panel, in the lower part of the glass (clear of the opening number).</summary>
    private static void RenderArrow(ViewportDrawingContext context, OpeningType type, Rect r, double scale)
    {
        bool horizontal = type is OpeningType.SlidingLeft or OpeningType.SlidingRight;
        double length = Math.Min(70 * scale, (horizontal ? r.Width : r.Height) * 0.5);
        if (length < 6) return;
        double centreY = scale < 1 ? (r.Top + r.Bottom) / 2 : r.Bottom - Math.Min(r.Height * 0.25, 60);
        var centre = new Point((r.Left + r.Right) / 2, horizontal ? centreY : (r.Top + r.Bottom) / 2);
        if (!horizontal && scale >= 1)
            centre = new Point(r.Left + Math.Min(r.Width * 0.25, 40), (r.Top + r.Bottom) / 2);
        var direction = type switch
        {
            OpeningType.SlidingLeft => new Vector(-1, 0),
            OpeningType.SlidingRight => new Vector(1, 0),
            OpeningType.SlidingUp => new Vector(0, -1),
            _ => new Vector(0, 1)
        };
        var tail = centre - direction * length / 2;
        var tip = centre + direction * length / 2;
        var side = new Vector(-direction.Y, direction.X);
        double head = Math.Max(3, 7.0 * scale);

        var dc = context.DrawingContext;
        dc.DrawLine(DesignTheme.SlideArrowPen, tail, tip);
        dc.DrawLine(DesignTheme.SlideArrowPen, tip, tip - direction * head + side * head * 0.6);
        dc.DrawLine(DesignTheme.SlideArrowPen, tip, tip - direction * head - side * head * 0.6);
    }

    private static void RenderHandle(ViewportDrawingContext context, Point2D world, HandleSide side, double scale)
    {
        var p = context.ToScreen(world);
        bool vertical = side is HandleSide.Left or HandleSide.Right;
        double thin = Math.Max(2, 5 * scale), length = Math.Max(5, 16 * scale);
        double w = vertical ? thin : length, h = vertical ? length : thin;
        context.DrawingContext.DrawRectangle(DesignTheme.HandleBrush, null, new Rect(p.X - w / 2, p.Y - h / 2, w, h));
    }

    /// <summary>
    /// The four outer members share one fill; outlines are drawn only on the outer edge and the inner
    /// opening, so the corners read as one continuous frame.
    /// </summary>
    private static void RenderOuterFrame(ViewportDrawingContext context, Frame frame, Vector2D origin, bool selected)
    {
        foreach (var member in frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame))
            context.DrawRectangle(DesignTheme.ProfileFill, null, member.GetBounds().Offset(origin));

        var outline = selected ? DesignTheme.SelectionOutline : DesignTheme.ProfileOutline;
        context.DrawRectangle(null, outline, frame.Bounds);
        if (FrameMembers.Find(frame) is { } outer)
        {
            var inner = outer.InnerOpening.Offset(origin);
            context.DrawRectangle(null, outline, inner);
            // Mitred corners, as on the reference drawings.
            var o = ToRect(context, frame.Bounds);
            var i = ToRect(context, inner);
            var dc = context.DrawingContext;
            dc.DrawLine(DesignTheme.ProfileOutline, o.TopLeft, i.TopLeft);
            dc.DrawLine(DesignTheme.ProfileOutline, o.TopRight, i.TopRight);
            dc.DrawLine(DesignTheme.ProfileOutline, o.BottomLeft, i.BottomLeft);
            dc.DrawLine(DesignTheme.ProfileOutline, o.BottomRight, i.BottomRight);
        }
    }

    // ── Labels ──────────────────────────────────────────────────────

    private void RenderLabels(ViewportDrawingContext context, Frame frame, int frameNumber, Vector2D origin, bool sizes)
    {
        int sashNumber = 0, meshNumber = 0;
        for (int i = 0; i < frame.GlassPanels.Count; i++)
        {
            var glass = frame.GlassPanels[i];
            var sash = OpeningGeometry.SashOf(frame, glass, _rules);
            var area = (sash?.Glass ?? glass.Boundary).Offset(origin);
            var r = ToRect(context, area);
            if (r.Width < MinLabelWidthPixels || r.Height < MinLabelHeightPixels) continue;

            var centre = new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            DrawNumberCircle(context, (i + 1).ToString(CultureInfo.InvariantCulture), centre);

            if (sizes && r.Width >= MinSizeLabelWidthPixels && r.Height >= MinSizeLabelHeightPixels)
            {
                var text = context.CreateText(SizeText(glass.Boundary), DesignTheme.GlassLabel, DesignTheme.GlassLabelFontSize);
                context.DrawTextCentered(text, new Point(centre.X, centre.Y + 20));
            }

            // Tags along the top of the opening: S1 (sash), M1 (mesh shutter).
            var tags = new List<string>();
            if (sash is not null) tags.Add("S" + ++sashNumber);
            if (glass.HasMesh) tags.Add("M" + ++meshNumber);
            double y = r.Top + 12;
            foreach (string tag in tags)
            {
                DrawTag(context, tag, new Point(centre.X, y));
                y += 22;
            }

            if (sash is { HandleHeightMm: { } handleHeight } s && r.Height >= MinSizeLabelHeightPixels)
            {
                var handle = context.ToScreen(s.Handle + origin);
                var text = context.CreateText("HH = " + Format(handleHeight), DesignTheme.TagText, DesignTheme.GlassLabelFontSize);
                double x = s.Side == HandleSide.Right ? handle.X - 10 - text.Width : handle.X + 10;
                if (text.Width + 14 < r.Width / 2)
                    DrawBoxedText(context, text, new Point(x, handle.Y + 34));
            }
        }

        if (FrameMembers.Find(frame) is { } outer)
        {
            var inner = ToRect(context, outer.InnerOpening.Offset(origin));
            if (inner.Width >= MinLabelWidthPixels && inner.Height >= MinLabelHeightPixels)
            {
                var text = context.CreateText("F" + frameNumber, DesignTheme.TagText, DesignTheme.TagFontSize);
                DrawBoxedText(context, text, new Point(inner.Right - text.Width - 9, inner.Bottom - text.Height - 7));
            }
        }
    }

    private static void RenderGlassSize(ViewportDrawingContext context, Rectangle2D world, Rectangle2D size)
    {
        var r = ToRect(context, world);
        if (r.Width < MinSizeLabelWidthPixels || r.Height < 24) return;
        var text = context.CreateText(SizeText(size), DesignTheme.GlassLabel, DesignTheme.GlassLabelFontSize);
        context.DrawTextCentered(text, new Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
    }

    private static void DrawNumberCircle(ViewportDrawingContext context, string number, Point centre)
    {
        const double radius = 10;
        var dc = context.DrawingContext;
        dc.DrawEllipse(DesignTheme.TagFill, DesignTheme.TagPen, centre, radius, radius);
        var text = context.CreateText(number, DesignTheme.TagText, DesignTheme.TagFontSize);
        context.DrawTextCentered(text, centre);
    }

    private static void DrawTag(ViewportDrawingContext context, string tag, Point centre)
    {
        var text = context.CreateText(tag, DesignTheme.TagText, DesignTheme.TagFontSize);
        DrawBoxedText(context, text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2 + 4));
    }

    private static void DrawBoxedText(ViewportDrawingContext context, FormattedText text, Point topLeft)
    {
        var box = new Rect(topLeft.X - 4, topLeft.Y - 2, text.Width + 8, text.Height + 4);
        context.DrawingContext.DrawRectangle(DesignTheme.TagFill, DesignTheme.TagPen, box);
        context.DrawingContext.DrawText(text, topLeft);
    }

    /// <summary>"W1 × 2 · Bedroom" above the top dimension.</summary>
    private static void RenderReference(ViewportDrawingContext context, Frame frame)
    {
        var info = frame.Design;
        if (string.IsNullOrWhiteSpace(info.Reference)) return;
        string label = info.Reference + (info.Quantity > 1 ? $" × {info.Quantity}" : "")
                       + (string.IsNullOrWhiteSpace(info.Location) ? "" : " · " + info.Location);
        var text = context.CreateText(label, DesignTheme.ReferenceText, DesignTheme.ReferenceFontSize);
        text.SetFontWeight(FontWeights.SemiBold);
        var topLeft = context.ToScreen(new Point2D(frame.X, frame.Y));
        context.DrawingContext.DrawText(text,
            new Point(topLeft.X, topLeft.Y - DesignTheme.DimensionOffsetPixels - text.Height - 14));
    }

    /// <summary>A hatched floor line under the frame at its sill height, with the height written beside it.</summary>
    private static void RenderFloor(ViewportDrawingContext context, Frame frame)
    {
        if (frame.Design.FloorDistanceMm is not { } distance || !double.IsFinite(distance)) return;
        double overhang = FloorOverhang(frame);
        double floorY = frame.Y + frame.Height + distance;
        var a = context.ToScreen(new Point2D(frame.X - overhang, floorY));
        var b = context.ToScreen(new Point2D(frame.X + frame.Width + overhang, floorY));
        var dc = context.DrawingContext;

        dc.DrawLine(DesignTheme.FloorPen, a, b);
        for (double x = a.X; x < b.X; x += 8)
            dc.DrawLine(DesignTheme.FloorHatchPen, new Point(x, a.Y + 2), new Point(x - 6, a.Y + 9));

        var text = context.CreateText($"Floor distance = {Format(distance)}", DesignTheme.FloorLabel, DesignTheme.DimensionFontSize);
        var marker = new Point(b.X + 14, a.Y);
        var triangle = new StreamGeometry();
        using (var g = triangle.Open())
        {
            g.BeginFigure(new Point(marker.X - 6, marker.Y - 10), true, true);
            g.LineTo(new Point(marker.X + 6, marker.Y - 10), true, false);
            g.LineTo(marker, true, false);
        }
        triangle.Freeze();
        dc.DrawGeometry(DesignTheme.HandleBrush, null, triangle);
        DrawBoxedText(context, text, new Point(marker.X + 12, marker.Y - text.Height - 6));
    }

    private static double FloorOverhang(Frame frame) => Math.Max(100, frame.Width * 0.12);

    private static Rect ToRect(ViewportDrawingContext context, Rectangle2D world)
        => new(context.ToScreen(world.TopLeft), context.ToScreen(world.BottomRight));

    private static string SizeText(Rectangle2D size)
        => string.Create(CultureInfo.InvariantCulture, $"{size.Width:0.#} × {size.Height:0.#}");

    private static string Format(double mm) => mm.ToString("0.#", CultureInfo.InvariantCulture);
}
