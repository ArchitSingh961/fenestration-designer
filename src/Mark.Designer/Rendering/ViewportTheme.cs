using System.Windows.Media;

namespace Mark.Designer.Rendering;

/// <summary>
/// Shared, frozen pens and brushes for the viewport. Frozen resources are immutable and thread-safe,
/// and WPF can render them without change tracking. Create them once here, never per frame.
/// Pen widths are in screen pixels (DIPs), so lines keep a constant on-screen weight at every zoom.
/// </summary>
public static class ViewportTheme
{
    public static readonly Brush Background = Brush(0xF1, 0xF2, 0xF4);

    public static readonly Pen MinorGridPen = Pen(0xE1, 0xE3, 0xE7, 1.0);
    public static readonly Pen MajorGridPen = Pen(0xCC, 0xD0, 0xD6, 1.0);

    /// <summary>World X axis (the line Y = 0). CAD convention: X is red.</summary>
    public static readonly Pen XAxisPen = Pen(0xD9, 0x7A, 0x7A, 1.0);

    /// <summary>World Y axis (the line X = 0). CAD convention: Y is green.</summary>
    public static readonly Pen YAxisPen = Pen(0x6F, 0xAE, 0x7C, 1.0);

    public static readonly Brush RulerLabelBrush = Brush(0x9A, 0x9A, 0x9A);
    public static readonly Brush OriginLabelBrush = Brush(0x70, 0x70, 0x70);

    public static readonly Typeface LabelTypeface = new("Segoe UI");
    public const double LabelFontSize = 10.0;

    private static Brush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(Brush(r, g, b), thickness);
        pen.Freeze();
        return pen;
    }
}
