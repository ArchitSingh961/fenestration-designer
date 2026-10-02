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
    Generic
}
