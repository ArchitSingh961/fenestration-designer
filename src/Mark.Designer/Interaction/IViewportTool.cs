using Mark.Core.Geometry;

namespace Mark.Designer.Interaction;

/// <summary>Cursor a tool asks the viewport to show.</summary>
public enum ViewportCursor
{
    /// <summary>The viewport's normal crosshair.</summary>
    Default,

    /// <summary>Over or dragging something that moves left/right (a mullion, the frame's right handle).</summary>
    ResizeHorizontal,

    /// <summary>Over or dragging something that moves up/down (a transom, the frame's bottom handle).</summary>
    ResizeVertical,

    /// <summary>Over or dragging the frame's bottom-right corner handle.</summary>
    ResizeDiagonal,

    /// <summary>Over or dragging something that moves freely (a whole frame).</summary>
    Move,

    /// <summary>Panning.</summary>
    Hand
}

/// <summary>Keys the viewport forwards to the active tool.</summary>
public enum ViewportKey
{
    Escape,
    Delete,

    /// <summary>Ctrl+A (only while the drawing view has focus, so it never steals select-all from text boxes).</summary>
    SelectAll
}

/// <summary>
/// A left-button pointer event in the viewport, already converted to world coordinates through
/// <c>ViewportTransform.ScreenToWorld</c>. Tools work in world mm; <see cref="Screen"/> is only for
/// pixel-based thresholds (e.g. "has the mouse moved 3 px yet?").
/// </summary>
public sealed class ViewportPointerEventArgs
{
    public ViewportPointerEventArgs(Point2D screen, Point2D world, bool isControlPressed)
        : this(screen, world, isControlPressed, isShiftPressed: false) { }

    public ViewportPointerEventArgs(Point2D screen, Point2D world, bool isControlPressed, bool isShiftPressed)
    {
        Screen = screen;
        World = world;
        IsControlPressed = isControlPressed;
        IsShiftPressed = isShiftPressed;
    }

    public Point2D Screen { get; }
    public Point2D World { get; }
    public bool IsControlPressed { get; }
    public bool IsShiftPressed { get; }

    /// <summary>Set by the tool when it consumed the event.</summary>
    public bool Handled { get; set; }

    /// <summary>Set by the tool to change the cursor.</summary>
    public ViewportCursor Cursor { get; set; } = ViewportCursor.Default;
}

/// <summary>
/// An editing tool driven by the viewport (the "ITool" of the interaction architecture). The viewport keeps
/// zoom/pan (wheel, middle drag, Space+drag) for itself and forwards left-button and key input to the
/// active tool. Tools never touch WPF elements: they read world coordinates, run Core operations and
/// commit commands.
/// </summary>
public interface IViewportTool
{
    /// <summary>True while the tool wants all mouse input (e.g. during a drag); the viewport captures the mouse.</summary>
    bool IsCapturing { get; }

    void OnPointerDown(ViewportPointerEventArgs e);

    /// <summary>Called for every mouse move, pressed or not, so tools can show hover feedback and previews.</summary>
    void OnPointerMove(ViewportPointerEventArgs e);

    void OnPointerUp(ViewportPointerEventArgs e);

    /// <returns>True if the tool handled the key.</returns>
    bool OnKey(ViewportKey key);

    /// <summary>Abandons any operation in progress without changing the model (tool switch, lost capture).</summary>
    void Cancel();
}
