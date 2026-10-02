namespace Mark.Core.Models;

/// <summary>
/// How the sash in one opening (glass panel) of a frame opens. Directions are as seen from the INSIDE of the
/// building (the default view): "left" means the left edge as you stand inside looking out.
/// </summary>
public enum OpeningType
{
    /// <summary>Glazed directly into the frame; does not open.</summary>
    Fixed,

    /// <summary>Casement hinged on the left edge (handle on the right).</summary>
    SideHungLeft,

    /// <summary>Casement hinged on the right edge (handle on the left).</summary>
    SideHungRight,

    /// <summary>Hinged on the top edge (awning; handle at the bottom).</summary>
    TopHung,

    /// <summary>Hinged on the bottom edge (hopper; handle at the top).</summary>
    BottomHung,

    /// <summary>Turns on left hinges and tilts inwards from the bottom (handle on the right).</summary>
    TiltTurnLeft,

    /// <summary>Turns on right hinges and tilts inwards from the bottom (handle on the left).</summary>
    TiltTurnRight,

    /// <summary>Rotates about a vertical centre axis (handle on the right).</summary>
    PivotVertical,

    /// <summary>Rotates about a horizontal centre axis (handle at the bottom).</summary>
    PivotHorizontal,

    /// <summary>Sliding panel that opens towards the left.</summary>
    SlidingLeft,

    /// <summary>Sliding panel that opens towards the right.</summary>
    SlidingRight,

    /// <summary>Vertically sliding panel that opens upwards.</summary>
    SlidingUp,

    /// <summary>Vertically sliding panel that opens downwards.</summary>
    SlidingDown
}

/// <summary>Facts about <see cref="OpeningType"/> that rendering, validation and calculation share.</summary>
public static class OpeningTypes
{
    /// <summary>True for every type that has a sash (anything except <see cref="OpeningType.Fixed"/>).</summary>
    public static bool IsOpenable(this OpeningType type) => type != OpeningType.Fixed;

    public static bool IsSliding(this OpeningType type)
        => type is OpeningType.SlidingLeft or OpeningType.SlidingRight or OpeningType.SlidingUp or OpeningType.SlidingDown;

    /// <summary>Hinged or pivoting sashes (they have a handle at a height and swing open).</summary>
    public static bool IsHinged(this OpeningType type) => type.IsOpenable() && !type.IsSliding();

    /// <summary>The same opening seen from the other side of the wall: left and right swap, up and down do not.</summary>
    public static OpeningType Mirrored(this OpeningType type) => type switch
    {
        OpeningType.SideHungLeft => OpeningType.SideHungRight,
        OpeningType.SideHungRight => OpeningType.SideHungLeft,
        OpeningType.TiltTurnLeft => OpeningType.TiltTurnRight,
        OpeningType.TiltTurnRight => OpeningType.TiltTurnLeft,
        OpeningType.SlidingLeft => OpeningType.SlidingRight,
        OpeningType.SlidingRight => OpeningType.SlidingLeft,
        _ => type
    };

    /// <summary>Short user-facing name, e.g. "Side hung (hinged left)".</summary>
    public static string DisplayName(this OpeningType type) => type switch
    {
        OpeningType.Fixed => "Fixed",
        OpeningType.SideHungLeft => "Side hung (hinged left)",
        OpeningType.SideHungRight => "Side hung (hinged right)",
        OpeningType.TopHung => "Top hung",
        OpeningType.BottomHung => "Bottom hung",
        OpeningType.TiltTurnLeft => "Tilt & turn (hinged left)",
        OpeningType.TiltTurnRight => "Tilt & turn (hinged right)",
        OpeningType.PivotVertical => "Vertical pivot",
        OpeningType.PivotHorizontal => "Horizontal pivot",
        OpeningType.SlidingLeft => "Sliding (opens left)",
        OpeningType.SlidingRight => "Sliding (opens right)",
        OpeningType.SlidingUp => "Vertical sliding (opens up)",
        OpeningType.SlidingDown => "Vertical sliding (opens down)",
        _ => type.ToString()
    };
}
