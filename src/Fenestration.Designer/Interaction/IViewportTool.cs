using Fenestration.Core.Geometry;

namespace Fenestration.Designer.Interaction;

/// <summary>Cursor a tool asks the viewport to show.</summary>
public enum ViewportCursor
{
    /// <summary>The viewport's normal crosshair.</summary>
    Default,

    /// <summary>Over or dragging something that moves left/right (a mullion).</summary>
    ResizeHorizontal,

    /// <summary>Over or dragging something that moves up/down (a transom).</summary>
    ResizeVertical
}

/// <summary>Keys the viewport forwards to the active tool.</summary>
public enum ViewportKey
{
    Escape,
    Delete
}

/// <summary>
/// A left-button pointer event in the viewport, already converted to world coordinates through
/// <c>ViewportTransform.ScreenToWorld</c>. Tools work in world mm; <see cref="Screen"/> is only for
/// pixel-based thresholds (e.g. "has the mouse moved 3 px yet?").
/// </summary>
public sealed class ViewportPointerEventArgs
{
    public ViewportPointerEventArgs(Point2D screen, Point2D world, bool isControlPressed)
    {
        Screen = screen;
        World = world;
        IsControlPressed = isControlPressed;
    }

    public Point2D Screen { get; }
    public Point2D World { get; }
    public bool IsControlPressed { get; }

    /// <summary>Set by the tool when it consumed the event.</summary>
    public bool Handled { get; set; }

    /// <summary>Set by the tool to change the cursor.</summary>
    public ViewportCursor Cursor { get; set; } = ViewportCursor.Default;
}

/// <summary>
/// An editing tool driven by the viewport. The viewport keeps zoom/pan (wheel, middle drag, Space+drag)
/// for itself and forwards left-button and key input to the active tool.
/// </summary>
public interface IViewportTool
{
    /// <summary>True while the tool wants all mouse input (e.g. during a drag); the viewport captures the mouse.</summary>
    bool IsCapturing { get; }

    void OnPointerDown(ViewportPointerEventArgs e);

    /// <summary>Called for every mouse move, pressed or not, so tools can show hover feedback.</summary>
    void OnPointerMove(ViewportPointerEventArgs e);

    void OnPointerUp(ViewportPointerEventArgs e);

    /// <returns>True if the tool handled the key.</returns>
    bool OnKey(ViewportKey key);
}
