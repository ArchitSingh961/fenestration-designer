namespace Mark.Core.Models;

/// <summary>
/// Categorises the role of a profile within a frame.
/// </summary>
public enum ProfileType
{
    /// <summary>Outer frame member.</summary>
    Frame,

    /// <summary>Vertical divider between openings.</summary>
    Mullion,

    /// <summary>Horizontal divider between openings.</summary>
    Transom,

    /// <summary>Openable sash member (the glass sash of an opening).</summary>
    Sash,

    /// <summary>Member of an insect-mesh shutter.</summary>
    MeshSash,

    /// <summary>Application-specific or unclassified profile.</summary>
    Generic,

    /// <summary>Steel (or other) reinforcement inside a profile; cut and listed with the bar it reinforces.</summary>
    Reinforcement,

    /// <summary>Glazing bead that holds the glass.</summary>
    GlazingBead,

    /// <summary>Interlock of sliding sashes.</summary>
    Interlock,

    /// <summary>Track rail of a sliding frame.</summary>
    Track,

    /// <summary>Coupler joining two frames, or an add-on section such as a cover or sill.</summary>
    Coupler
}
