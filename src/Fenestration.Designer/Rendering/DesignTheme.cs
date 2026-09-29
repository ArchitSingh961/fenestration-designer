using System.Windows.Media;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Frozen pens and brushes for design content (frames, divisions, glass, dimensions). Profiles are solid
/// grey bodies with a dark outline, glass is a light translucent blue, and the selection is blue.
/// Pen widths are screen pixels, so line weights stay constant at every zoom.
/// </summary>
public static class DesignTheme
{
    public static readonly Brush ProfileFill = Brush(0xD4, 0xD8, 0xDD);
    public static readonly Pen ProfileOutline = Pen(Color.FromRgb(0x4A, 0x52, 0x5C), 1.0);

    public static readonly Brush GlassFill = Brush(Color.FromArgb(0x55, 0xBF, 0xDB, 0xF0));
    public static readonly Pen GlassOutline = Pen(Color.FromRgb(0x8F, 0xB4, 0xD0), 0.75);
    public static readonly Brush GlassLabel = Brush(0x5A, 0x7A, 0x96);

    public static readonly Brush SelectedProfileFill = Brush(0xA9, 0xC8, 0xF0);
    public static readonly Brush SelectedGlassFill = Brush(Color.FromArgb(0x80, 0x7F, 0xB3, 0xEE));
    public static readonly Pen SelectionOutline = Pen(Color.FromRgb(0x1E, 0x6F, 0xD9), 2.0);

    public static readonly Pen DimensionPen = Pen(Color.FromRgb(0x55, 0x5D, 0x66), 0.75);
    public static readonly Brush DimensionText = Brush(0x30, 0x36, 0x3C);

    public const double DimensionFontSize = 11.0;
    public const double GlassLabelFontSize = 10.0;

    /// <summary>Distance (px) from the frame edge to its dimension line.</summary>
    public const double DimensionOffsetPixels = 30.0;

    /// <summary>Gap (px) between the frame and the start of an extension line.</summary>
    public const double ExtensionGapPixels = 4.0;

    /// <summary>How far (px) an extension line runs past its dimension line.</summary>
    public const double ExtensionOvershootPixels = 4.0;

    /// <summary>Half-length (px) of the 45° tick at each end of a dimension line.</summary>
    public const double TickHalfPixels = 3.5;

    private static Brush Brush(byte r, byte g, byte b) => Brush(Color.FromRgb(r, g, b));

    private static Brush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(Color color, double thickness)
    {
        var pen = new Pen(Brush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
