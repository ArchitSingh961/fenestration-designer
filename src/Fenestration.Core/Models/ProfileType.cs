namespace Fenestration.Core.Models;

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

    /// <summary>Openable sash member.</summary>
    Sash,

    /// <summary>Application-specific or unclassified profile.</summary>
    Generic
}
