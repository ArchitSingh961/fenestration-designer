namespace Fenestration.Core.Models;

/// <summary>
/// The product facts of one frame (a "design" or window type in a quote): reference, quantity and where it goes.
/// Plain data; validated by <see cref="Design.FrameEditor.TrySetDesignInfo"/>.
/// </summary>
public sealed class DesignInfo
{
    /// <summary>Design reference shown on drawings and quotes, e.g. "W1" or "D2". Empty = not set.</summary>
    public string Reference { get; set; } = "";

    /// <summary>How many of this design are needed (at least 1).</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Optional descriptive name, e.g. "Bedroom window".</summary>
    public string Name { get; set; } = "";

    /// <summary>Where it is installed, e.g. "Bedroom 2".</summary>
    public string Location { get; set; } = "";

    /// <summary>Floor, e.g. "Ground" or "3".</summary>
    public string Floor { get; set; } = "";

    public string Note { get; set; } = "";

    /// <summary>
    /// Height of the bottom of the frame above the finished floor (sill height), in mm. Null = not specified,
    /// and no floor line is drawn.
    /// </summary>
    public double? FloorDistanceMm { get; set; }

    public DesignInfo Copy() => (DesignInfo)MemberwiseClone();
}
