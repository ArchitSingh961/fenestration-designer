using System.Windows.Media;

namespace Fenestration.Designer.Rendering;

/// <summary>
/// Frozen pens and brushes for design content (frames, divisions, glass, dimensions). Profiles are solid
/// grey bodies with a dark outline, glass is a light translucent blue, and the selection is blue.
/// Pen widths are screen pixels, so line weights stay constant at every zoom.
/// </summary>
public static class DesignTheme
{
    public static readonly Brush ProfileFill = Brush(0xB9, 0xBE, 0xC5);
    public static readonly Pen ProfileOutline = Pen(Color.FromRgb(0x4A, 0x52, 0x5C), 1.0);

    public static readonly Brush GlassFill = Brush(Color.FromArgb(0x99, 0xA9, 0xD8, 0xEC));
    public static readonly Pen GlassOutline = Pen(Color.FromRgb(0x7F, 0xA9, 0xC6), 0.75);
    public static readonly Brush GlassLabel = Brush(0x3F, 0x5F, 0x7A);

    // ── Sashes and opening symbols ──────────────────────────────────

    /// <summary>Sash band: a little lighter than the frame, so the sash reads as a separate part.</summary>
    public static readonly Brush SashFill = Brush(0xD0, 0xD4, 0xD9);
    public static readonly Pen SashOutline = Pen(Color.FromRgb(0x4A, 0x52, 0x5C), 1.0);

    /// <summary>Dashed lines of the opening symbol (they meet at the handle side).</summary>
    public static readonly Pen OpeningSymbolPen = DashedPen(Color.FromRgb(0x52, 0x5A, 0x64), 1.0);
    public static readonly Pen PivotAxisPen = Pen(Color.FromRgb(0x52, 0x5A, 0x64), 0.75, DashStyles.DashDot);
    public static readonly Pen SlideArrowPen = Pen(Color.FromRgb(0x3C, 0x43, 0x4B), 1.5);
    public static readonly Brush HandleBrush = Brush(0x3C, 0x43, 0x4B);
    public static readonly Pen MeshPen = Pen(Color.FromArgb(0x8C, 0x55, 0x5D, 0x66), 0.75);

    /// <summary>Opening-number circles and S/M/F tags.</summary>
    public static readonly Brush TagFill = Brush(0xFF, 0xFF, 0xFF);
    public static readonly Pen TagPen = Pen(Color.FromRgb(0x3C, 0x43, 0x4B), 1.0);
    public static readonly Brush TagText = Brush(0x26, 0x2C, 0x33);
    public const double TagFontSize = 11.0;

    /// <summary>Design reference above the frame ("W1 × 2").</summary>
    public static readonly Brush ReferenceText = Brush(0x1F, 0x3A, 0x5F);
    public const double ReferenceFontSize = 13.0;

    public static readonly Pen FloorPen = Pen(Color.FromRgb(0x5A, 0x60, 0x68), 3.0);
    public static readonly Pen FloorHatchPen = Pen(Color.FromRgb(0x8A, 0x90, 0x98), 1.0);
    public static readonly Brush FloorLabel = Brush(0x30, 0x36, 0x3C);

    /// <summary>The opening a library design is being dragged onto.</summary>
    public static readonly Brush DropTargetFill = Brush(Color.FromArgb(0x70, 0xF0, 0xD9, 0xB5));
    public static readonly Pen DropTargetPen = Pen(Color.FromRgb(0xD9, 0x8C, 0x1E), 2.0);

    public static readonly Brush SelectedProfileFill = Brush(0xA9, 0xC8, 0xF0);
    public static readonly Brush SelectedGlassFill = Brush(Color.FromArgb(0x80, 0x7F, 0xB3, 0xEE));
    public static readonly Pen SelectionOutline = Pen(Color.FromRgb(0x1E, 0x6F, 0xD9), 2.0);

    public static readonly Pen DimensionPen = Pen(Color.FromRgb(0x55, 0x5D, 0x66), 0.75);
    public static readonly Brush DimensionText = Brush(0x30, 0x36, 0x3C);

    // ── Interaction feedback (overlay) ──────────────────────────────

    /// <summary>Window selection (left → right, "inside"): solid blue.</summary>
    public static readonly Brush WindowSelectionFill = Brush(Color.FromArgb(0x22, 0x1E, 0x6F, 0xD9));
    public static readonly Pen WindowSelectionPen = Pen(Color.FromRgb(0x1E, 0x6F, 0xD9), 1.0);

    /// <summary>Crossing selection (right → left, "touching"): dashed green.</summary>
    public static readonly Brush CrossingSelectionFill = Brush(Color.FromArgb(0x22, 0x2E, 0xA0, 0x4F));
    public static readonly Pen CrossingSelectionPen = DashedPen(Color.FromRgb(0x2E, 0xA0, 0x4F), 1.0);

    /// <summary>Something valid that doesn't exist yet (e.g. the frame being drawn).</summary>
    public static readonly Brush GhostFill = Brush(Color.FromArgb(0x30, 0x1E, 0x6F, 0xD9));
    public static readonly Pen GhostPen = DashedPen(Color.FromRgb(0x1E, 0x6F, 0xD9), 1.5);

    /// <summary>An invalid candidate: shown, never committed.</summary>
    public static readonly Brush InvalidFill = Brush(Color.FromArgb(0x40, 0xD9, 0x3A, 0x3A));
    public static readonly Pen InvalidPen = DashedPen(Color.FromRgb(0xC6, 0x28, 0x28), 1.5);

    public static readonly Pen SnapMarkerPen = Pen(Color.FromRgb(0xE0, 0x7B, 0x00), 1.75);
    public static readonly Pen SnapAlignPen = DashedPen(Color.FromRgb(0xE0, 0x7B, 0x00), 0.75);
    public static readonly Brush SnapLabel = Brush(0xB0, 0x5E, 0x00);

    public static readonly Brush HandleFill = Brush(0xFF, 0xFF, 0xFF);
    public static readonly Pen HandlePen = Pen(Color.FromRgb(0x1E, 0x6F, 0xD9), 1.5);

    /// <summary>Half-size (px) of a resize handle square; also its grab radius.</summary>
    public const double HandleHalfPixels = 5.0;

    /// <summary>Half-size (px) of a snap marker.</summary>
    public const double SnapMarkerHalfPixels = 6.0;

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

    private static Pen Pen(Color color, double thickness, DashStyle dashStyle)
    {
        var pen = new Pen(Brush(color), thickness) { DashStyle = dashStyle };
        pen.Freeze();
        return pen;
    }

    private static Pen DashedPen(Color color, double thickness)
    {
        var pen = new Pen(Brush(color), thickness) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }
}
