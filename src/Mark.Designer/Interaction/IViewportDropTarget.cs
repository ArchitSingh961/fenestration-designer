using Mark.Core.Geometry;

namespace Mark.Designer.Interaction;

/// <summary>
/// Receives things dragged onto the drawing view (library designs), in world coordinates. The viewport only
/// converts the mouse position; deciding what is under it and what dropping does belongs to the view model.
/// </summary>
public interface IViewportDropTarget
{
    /// <summary>Clipboard/drag format of a library design; the data is the design's template Id (a string).</summary>
    public const string DesignFormat = "Mark.DesignTemplate";

    /// <summary>The drag moved over <paramref name="world"/>. Returns true if a drop there would be accepted.</summary>
    bool DragOver(Point2D world, string templateId);

    /// <summary>The drag left the view or was cancelled.</summary>
    void DragLeave();

    /// <summary>Drops the design at <paramref name="world"/>. Returns true if it was applied.</summary>
    bool Drop(Point2D world, string templateId);
}
