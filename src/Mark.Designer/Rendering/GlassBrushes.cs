using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Mark.Core.Library;

namespace Mark.Designer.Rendering;

/// <summary>
/// The fill of a glass pane for its <see cref="GlassLook"/>: clear and tinted glass as a see-through colour, frosted as
/// a milky stipple, reflective as a shine, patterned as fine diagonal lines and designer glass as a diamond lattice.
/// Patterns are tiled in screen pixels, so they read the same at every zoom. Brushes are frozen and cached.
/// </summary>
public static class GlassBrushes
{
    private static readonly Color DefaultTint = Color.FromRgb(0xA9, 0xD8, 0xEC);
    private static readonly Dictionary<(GlassPattern, Color, bool), Brush> Cache = new();
    private static readonly object Gate = new();

    /// <summary>The fill for a pane with this look (null: the usual clear glass).</summary>
    public static Brush For(GlassLook? look, bool selected = false)
    {
        if (look is null || (look.Pattern == GlassPattern.Clear && look.Color is null))
            return selected ? DesignTheme.SelectedGlassFill : DesignTheme.GlassFill;
        var tint = ParseColor(look.Color) ?? DefaultTint;
        lock (Gate)
        {
            if (!Cache.TryGetValue((look.Pattern, tint, selected), out var brush))
            {
                brush = Create(look.Pattern, selected ? Blend(tint, Color.FromRgb(0x7F, 0xB3, 0xEE), 0.45) : tint);
                brush.Freeze();
                Cache[(look.Pattern, tint, selected)] = brush;
            }
            return brush;
        }
    }

    private static Brush Create(GlassPattern pattern, Color tint) => pattern switch
    {
        GlassPattern.Tinted => new SolidColorBrush(WithAlpha(tint, 0xC8)),
        GlassPattern.Frosted => Tile(WithAlpha(Blend(tint, Colors.White, 0.7), 0xE6), 6, (g, pen) =>
        {
            var dot = new SolidColorBrush(WithAlpha(Blend(tint, Colors.Gray, 0.35), 0x90));
            g.Children.Add(new GeometryDrawing(dot, null, new EllipseGeometry(new Point(1.5, 1.5), 0.7, 0.7)));
            g.Children.Add(new GeometryDrawing(dot, null, new EllipseGeometry(new Point(4.5, 4.5), 0.6, 0.6)));
        }),
        GlassPattern.Reflective => new LinearGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(Blend(tint, Colors.White, 0.75), 0xE0), 0.0),
            new(WithAlpha(tint, 0xD0), 0.45),
            new(WithAlpha(Blend(tint, Colors.White, 0.55), 0xD8), 0.55),
            new(WithAlpha(Blend(tint, Colors.Black, 0.25), 0xD0), 1.0)
        }, new Point(0, 0), new Point(1, 1)),
        GlassPattern.Patterned => Tile(WithAlpha(tint, 0xA8), 8, (g, pen) =>
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M0,8 L8,0 M-2,2 L2,-2 M6,10 L10,6")))),
        GlassPattern.Designer => Tile(WithAlpha(tint, 0xA8), 14, (g, pen) =>
            g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M7,0 L14,7 L7,14 L0,7 Z")))),
        _ => new SolidColorBrush(WithAlpha(tint, 0x99))
    };

    /// <summary>A pattern of <paramref name="size"/> px tiles on a background colour.</summary>
    private static Brush Tile(Color background, double size, Action<DrawingGroup, Pen> draw)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, new RectangleGeometry(new Rect(0, 0, size, size))));
        var pen = new Pen(new SolidColorBrush(WithAlpha(Blend(background, Colors.White, 0.55), 0xF0)), 1.0);
        draw(group, pen);
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    /// <summary>"#RRGGBB" as a colour, or null.</summary>
    public static Color? ParseColor(string? text)
    {
        if (text is not { Length: 7 } || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return null;
        return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t)
        => Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
