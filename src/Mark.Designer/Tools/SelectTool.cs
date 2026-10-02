using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Interaction;
using Mark.Designer.Interaction;
using Mark.Designer.ViewModels;

namespace Mark.Designer.Tools;

/// <summary>
/// The default tool: select, box-select, move and resize.
///
/// <b>Hit-test priority</b> (deterministic, never collection order):
/// <list type="number">
///   <item>resize handles of the single selected frame;</item>
///   <item>mullions / transoms (the nearest centreline wins);</item>
///   <item>glass panels;</item>
///   <item>the frame (its outer profiles / border).</item>
/// </list>
///
/// <b>Clicks:</b> click selects one object (and clears the rest); Shift+click adds; Ctrl+click toggles;
/// a click on empty space clears the selection (unless Shift/Ctrl is held).
/// <b>Drags:</b> from a handle → resize the frame; from a frame/mullion/transom → move the selection; from empty space →
/// box selection (left→right window = fully inside, right→left crossing = touching). Glass is derived and never moves.
///
/// Every drag goes Begin → preview (snap → validate) on each move → ONE command on release. Esc or lost capture
/// discards the preview; the model was never changed.
/// </summary>
public sealed class SelectTool : DesignerToolBase
{
    public const double HitTolerancePixels = 5.0;
    public const double DragThresholdPixels = 3.0;

    /// <summary>Handles can be grabbed a little outside their drawn square.</summary>
    public const double HandleGrabPixels = 7.0;

    private enum Gesture { None, PendingMove, Moving, PendingResize, Resizing, PendingBox, Boxing }

    private Gesture _gesture;
    private Point2D _startScreen;
    private Point2D _startWorld;
    private Guid _grabbedId;
    private DesignElementKind _grabbedKind;
    private Guid? _collapseTo;
    private bool _boxAdditive;
    private Rectangle2D? _box;
    private bool _boxCrossing;
    private FrameHandle _handle;
    private Guid _handleFrameId;
    private MoveElementsOperation? _move;
    private ResizeFrameOperation? _resize;

    public SelectTool(MainViewModel host) : base(host) { }

    public override bool IsCapturing => _gesture != Gesture.None;

    /// <summary>True once a drag (move, resize or box) has actually started.</summary>
    public bool IsDragging => _gesture is Gesture.Moving or Gesture.Resizing or Gesture.Boxing;

    public override void OnPointerDown(ViewportPointerEventArgs e)
    {
        if (_gesture != Gesture.None) Cancel();
        e.Handled = true;
        _startScreen = e.Screen;
        _startWorld = e.World;
        _collapseTo = null;

        // 1. Resize handles of the selected frame.
        if (Host.SingleSelectedFrame is { } selectedFrame
            && FrameHandles.HitTest(selectedFrame, e.World, PixelsToMm(HandleGrabPixels)) is { } handle)
        {
            _gesture = Gesture.PendingResize;
            _handle = handle;
            _handleFrameId = selectedFrame.Id;
            e.Cursor = CursorFor(handle);
            return;
        }

        // 2–4. Divisions, glass, frame.
        var hit = FrameHitTester.HitTest(Host.Project, e.World, PixelsToMm(HitTolerancePixels));
        if (hit is not { } h)
        {
            _boxAdditive = e.IsShiftPressed || e.IsControlPressed;
            if (!_boxAdditive) Host.ClearSelection();
            _gesture = Gesture.PendingBox;
            return;
        }

        if (e.IsControlPressed)
        {
            Host.Selection.Toggle(h.ElementId);
            return;
        }

        if (e.IsShiftPressed) Host.Selection.Add(h.ElementId);
        else if (!Host.IsSelected(h.ElementId)) Host.Select(h.ElementId);
        else if (Host.Selection.Count > 1) _collapseTo = h.ElementId;   // a click (not a drag) narrows to this object

        if (h.Kind != DesignElementKind.Glass)
        {
            _gesture = Gesture.PendingMove;
            _grabbedId = h.ElementId;
            _grabbedKind = h.Kind;
            e.Cursor = CursorFor(h.Kind);
        }
    }

    public override void OnPointerMove(ViewportPointerEventArgs e)
    {
        if (_gesture == Gesture.None)
        {
            e.Cursor = HoverCursor(e.World);
            return;
        }

        e.Handled = true;
        if (_gesture is Gesture.PendingMove or Gesture.PendingResize or Gesture.PendingBox)
        {
            if (_startScreen.DistanceTo(e.Screen) < DragThresholdPixels) return;
            BeginDrag();
            if (_gesture == Gesture.None) return;
        }

        switch (_gesture)
        {
            case Gesture.Moving:
                Host.Interaction.SetPreview(_move!.Update(e.World, Host.SnapToleranceMm));
                e.Cursor = CursorFor(_grabbedKind);
                break;
            case Gesture.Resizing:
                Host.Interaction.SetPreview(_resize!.Update(e.World, Host.SnapToleranceMm));
                e.Cursor = CursorFor(_handle);
                break;
            case Gesture.Boxing:
                _box = Rectangle2D.FromCorners(_startWorld, e.World);
                _boxCrossing = e.World.X < _startWorld.X;
                Host.Interaction.SetSelectionBox(_box, _boxCrossing);
                break;
        }
    }

    public override void OnPointerUp(ViewportPointerEventArgs e)
    {
        if (_gesture == Gesture.None) return;
        e.Handled = true;

        var gesture = _gesture;
        var command = gesture switch
        {
            Gesture.Moving => _move!.CreateCommand(),
            Gesture.Resizing => _resize!.CreateCommand(),
            _ => null
        };
        var box = _box;
        bool crossing = _boxCrossing, additive = _boxAdditive;
        var collapseTo = _collapseTo;
        Reset();

        switch (gesture)
        {
            case Gesture.Moving or Gesture.Resizing when command is not null:
                Host.Execute(command);
                break;
            case Gesture.Boxing when box is { } rect:
                var ids = SelectionQuery.InRectangle(Host.Project, rect, crossing);
                if (additive) Host.Selection.AddMany(ids);
                else Host.Selection.SelectMany(ids);
                break;
            case Gesture.PendingMove when collapseTo is { } id:
                Host.Select(id);
                break;
        }
    }

    public override void Cancel() => Reset();

    /// <summary>Esc with nothing in progress clears the selection.</summary>
    protected override bool OnEscapeIdle()
    {
        Host.ClearSelection();
        return true;
    }

    private void BeginDrag()
    {
        switch (_gesture)
        {
            case Gesture.PendingMove:
                _move = new MoveElementsOperation(Host.Project, Host.SelectedIds, _grabbedId, _startWorld, Host.Rules, Host.SnapEngine);
                _gesture = _move.IsEmpty ? Gesture.None : Gesture.Moving;
                break;
            case Gesture.PendingResize when Host.Project.Frames.FirstOrDefault(f => f.Id == _handleFrameId) is { } frame:
                _resize = new ResizeFrameOperation(Host.Project, frame, _handle, _startWorld, Host.Rules, Host.SnapEngine);
                _gesture = Gesture.Resizing;
                break;
            case Gesture.PendingBox:
                _gesture = Gesture.Boxing;
                break;
            default:
                _gesture = Gesture.None;
                break;
        }
        _collapseTo = null;
    }

    private void Reset()
    {
        _gesture = Gesture.None;
        _move = null;
        _resize = null;
        _box = null;
        _collapseTo = null;
        Host.Interaction.Clear();
    }

    private ViewportCursor HoverCursor(Point2D world)
    {
        if (Host.SingleSelectedFrame is { } frame && FrameHandles.HitTest(frame, world, PixelsToMm(HandleGrabPixels)) is { } handle)
            return CursorFor(handle);
        return FrameHitTester.HitTest(Host.Project, world, PixelsToMm(HitTolerancePixels)) is { } hit
            ? CursorFor(hit.Kind)
            : ViewportCursor.Default;
    }

    private static ViewportCursor CursorFor(DesignElementKind kind) => kind switch
    {
        DesignElementKind.Mullion => ViewportCursor.ResizeHorizontal,
        DesignElementKind.Transom => ViewportCursor.ResizeVertical,
        DesignElementKind.Frame => ViewportCursor.Move,
        _ => ViewportCursor.Default
    };

    private static ViewportCursor CursorFor(FrameHandle handle) => handle switch
    {
        FrameHandle.Right => ViewportCursor.ResizeHorizontal,
        FrameHandle.Bottom => ViewportCursor.ResizeVertical,
        _ => ViewportCursor.ResizeDiagonal
    };
}
