using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interaction;
using Fenestration.Designer.Interaction;
using Fenestration.Designer.ViewModels;

namespace Fenestration.Designer.Tools;

/// <summary>Left-drag pans the view (the domain model is never touched).</summary>
public sealed class PanTool : DesignerToolBase
{
    private Point2D? _last;

    public PanTool(MainViewModel host) : base(host) { }

    public override bool IsCapturing => _last is not null;

    public override void OnPointerDown(ViewportPointerEventArgs e)
    {
        _last = e.Screen;
        e.Cursor = ViewportCursor.Hand;
        e.Handled = true;
    }

    public override void OnPointerMove(ViewportPointerEventArgs e)
    {
        e.Cursor = ViewportCursor.Hand;
        if (_last is not { } last) return;
        Host.Canvas.Pan(e.Screen.X - last.X, e.Screen.Y - last.Y);
        _last = e.Screen;
        e.Handled = true;
    }

    public override void OnPointerUp(ViewportPointerEventArgs e)
    {
        _last = null;
        e.Handled = true;
    }

    public override void Cancel() => _last = null;
}

/// <summary>
/// Drag a rectangle to create a frame (the drawn equivalent of typing width × height). Corners snap; the size is
/// validated live; release creates the frame with one command. The tool stays active for the next frame.
/// </summary>
public sealed class FrameTool : DesignerToolBase
{
    private CreateFrameOperation? _operation;
    private Point2D _startScreen;
    private bool _dragged;

    public FrameTool(MainViewModel host) : base(host) { }

    public override bool IsCapturing => _operation is not null;

    public override void OnPointerDown(ViewportPointerEventArgs e)
    {
        e.Handled = true;
        _operation = new CreateFrameOperation(Host.Project, e.World, Host.SnapToleranceMm, Host.Rules, Host.SnapEngine);
        _startScreen = e.Screen;
        _dragged = false;
    }

    public override void OnPointerMove(ViewportPointerEventArgs e)
    {
        if (_operation is null) return;
        e.Handled = true;
        if (!_dragged && _startScreen.DistanceTo(e.Screen) < SelectTool.DragThresholdPixels) return;
        _dragged = true;
        Host.Interaction.SetPreview(_operation.Update(e.World, Host.SnapToleranceMm));
    }

    public override void OnPointerUp(ViewportPointerEventArgs e)
    {
        if (_operation is null) return;
        e.Handled = true;
        var message = Host.Interaction.Preview.IsValid ? null : Host.Interaction.Preview.Message;
        var command = _dragged ? _operation.CreateCommand() : null;
        Cancel();

        if (command is CreateFrameCommand create && Host.Execute(create) is null)
            Host.Select(create.Frame.Id);
        else if (_dragged && message is not null)
            Host.DesignMessage = message;
    }

    public override void Cancel()
    {
        _operation = null;
        _dragged = false;
        Host.Interaction.Clear();
    }
}

/// <summary>
/// Hover over a glass panel to preview a mullion or transom through that opening at the (snapped) pointer
/// position; click to add it. Hold Shift to span the whole frame instead. The tool stays active.
/// </summary>
public sealed class DivisionTool : DesignerToolBase
{
    private readonly MemberAxis _axis;
    private AddDivisionOperation? _operation;
    private int _operationVersion = -1;

    public DivisionTool(MainViewModel host, MemberAxis axis) : base(host) => _axis = axis;

    public MemberAxis Axis => _axis;

    public override bool IsCapturing => false;

    public override void OnPointerMove(ViewportPointerEventArgs e)
    {
        Host.Interaction.SetPreview(Operation().Update(e.World, Host.SnapToleranceMm, e.IsShiftPressed));
    }

    public override void OnPointerDown(ViewportPointerEventArgs e)
    {
        e.Handled = true;
        var preview = Operation().Update(e.World, Host.SnapToleranceMm, e.IsShiftPressed);
        var command = _operation!.CreateCommand();
        Cancel();

        if (command is null)
        {
            Host.DesignMessage = preview.Message;
            return;
        }
        if (Host.Execute(command) is null && command is AddDivisionCommand { CreatedProfileId: { } id })
            Host.Select(id);
    }

    public override void Cancel()
    {
        _operation = null;
        Host.Interaction.Clear();
    }

    /// <summary>The operation caches snap targets, so it is rebuilt whenever the design changed.</summary>
    private AddDivisionOperation Operation()
    {
        if (_operation is null || _operationVersion != Host.DesignVersion)
        {
            _operation = new AddDivisionOperation(Host.Project, _axis, Host.Rules, Host.SnapEngine);
            _operationVersion = Host.DesignVersion;
        }
        return _operation;
    }
}
