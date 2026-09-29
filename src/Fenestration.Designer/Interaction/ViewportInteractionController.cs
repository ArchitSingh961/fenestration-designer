using System.Windows;
using System.Windows.Input;
using Fenestration.Core.Geometry;
using Fenestration.Designer.ViewModels;

namespace Fenestration.Designer.Interaction;

/// <summary>
/// Translates raw mouse/keyboard input on the viewport element into viewport operations, and forwards
/// editing input to the active <see cref="IViewportTool"/>. It holds only transient gesture state
/// (is a pan in progress, where did it start); view state lives in <see cref="CanvasViewModel"/>.
///
///   Mouse wheel            zoom around the cursor
///   Middle drag            pan
///   Space + left drag      pan
///   Left button            active tool (select / drag divisions)
///   F                      fit to screen
///   G                      toggle grid
///   Esc / Delete           active tool (cancel, clear selection / delete)
/// </summary>
public sealed class ViewportInteractionController
{
    private readonly FrameworkElement _element;
    private readonly Func<CanvasViewModel?> _viewport;
    private readonly Func<IViewportTool?> _tool;

    private bool _isPanning;
    private MouseButton _panButton;
    private Point _lastPanPosition;
    private bool _spaceHeld;
    private Cursor? _cursorBeforePan;

    public ViewportInteractionController(FrameworkElement element, Func<CanvasViewModel?> viewport, Func<IViewportTool?>? tool = null)
    {
        _element = element;
        _viewport = viewport;
        _tool = tool ?? (() => null);

        _element.MouseWheel += OnMouseWheel;
        _element.MouseDown += OnMouseDown;
        _element.MouseMove += OnMouseMove;
        _element.MouseUp += OnMouseUp;
        _element.MouseLeave += OnMouseLeave;
        _element.LostMouseCapture += OnLostMouseCapture;
        _element.KeyDown += OnKeyDown;
        _element.KeyUp += OnKeyUp;
    }

    public bool IsPanning => _isPanning;

    public void Detach()
    {
        _element.MouseWheel -= OnMouseWheel;
        _element.MouseDown -= OnMouseDown;
        _element.MouseMove -= OnMouseMove;
        _element.MouseUp -= OnMouseUp;
        _element.MouseLeave -= OnMouseLeave;
        _element.LostMouseCapture -= OnLostMouseCapture;
        _element.KeyDown -= OnKeyDown;
        _element.KeyUp -= OnKeyUp;
    }

    // ── Mouse ───────────────────────────────────────────────────────

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewport() is not { } vm) return;
        vm.ZoomAtWheel(ToPoint2D(e.GetPosition(_element)), e.Delta);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _element.Focus();

        bool startPan = e.ChangedButton == MouseButton.Middle
            || (e.ChangedButton == MouseButton.Left && _spaceHeld);
        if (startPan && !_isPanning)
        {
            _isPanning = true;
            _panButton = e.ChangedButton;
            _lastPanPosition = e.GetPosition(_element);
            _cursorBeforePan = _element.Cursor;
            _element.Cursor = Cursors.ScrollAll;
            _element.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && !_isPanning && _tool() is { } tool && CreateArgs(e) is { } args)
        {
            tool.OnPointerDown(args);
            ApplyCursor(args.Cursor);
            if (tool.IsCapturing) _element.CaptureMouse();
            e.Handled = args.Handled;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewport() is not { } vm) return;
        Point position = e.GetPosition(_element);

        if (_isPanning)
        {
            Vector delta = position - _lastPanPosition;
            _lastPanPosition = position;
            if (delta.X != 0 || delta.Y != 0)
                vm.Pan(delta.X, delta.Y);
        }
        else if (_tool() is { } tool && CreateArgs(e) is { } args)
        {
            tool.OnPointerMove(args);
            ApplyCursor(args.Cursor);
        }

        vm.UpdateCursor(ToPoint2D(position));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            if (e.ChangedButton != _panButton) return;
            EndPan();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _tool() is { } tool && CreateArgs(e) is { } args)
        {
            tool.OnPointerUp(args);
            if (!tool.IsCapturing && _element.IsMouseCaptured)
                _element.ReleaseMouseCapture();
            e.Handled = args.Handled;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning && _tool()?.IsCapturing != true)
            _viewport()?.ClearCursor();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture can be stolen (Alt+Tab, a dialog); make sure no gesture gets stuck.
        if (_isPanning)
            EndPan();
        else if (_tool() is { IsCapturing: true } tool)
            tool.OnKey(ViewportKey.Escape);
    }

    private void EndPan()
    {
        _isPanning = false;
        _element.Cursor = _cursorBeforePan;
        if (_element.IsMouseCaptured)
            _element.ReleaseMouseCapture();
    }

    // ── Keyboard (only while the viewport has focus, so shortcuts never fire in text boxes) ──

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewport() is not { } vm) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        switch (e.Key)
        {
            case Key.Space:
                _spaceHeld = true;
                e.Handled = true;
                break;
            case Key.F:
                vm.FitToContent();
                e.Handled = true;
                break;
            case Key.G:
                vm.ShowGrid = !vm.ShowGrid;
                e.Handled = true;
                break;
            case Key.Escape:
                e.Handled = _tool()?.OnKey(ViewportKey.Escape) == true;
                ReleaseIfToolDone();
                break;
            case Key.Delete:
                e.Handled = _tool()?.OnKey(ViewportKey.Delete) == true;
                break;
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            e.Handled = true;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private ViewportPointerEventArgs? CreateArgs(MouseEventArgs e)
    {
        if (_viewport() is not { } vm) return null;
        var screen = ToPoint2D(e.GetPosition(_element));
        return new ViewportPointerEventArgs(screen, vm.ScreenToWorld(screen), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    private void ReleaseIfToolDone()
    {
        if (!_isPanning && _tool()?.IsCapturing != true && _element.IsMouseCaptured)
            _element.ReleaseMouseCapture();
    }

    private void ApplyCursor(ViewportCursor cursor)
    {
        if (_isPanning) return;
        _element.Cursor = cursor switch
        {
            ViewportCursor.ResizeHorizontal => Cursors.SizeWE,
            ViewportCursor.ResizeVertical => Cursors.SizeNS,
            _ => Cursors.Cross
        };
    }

    private static Point2D ToPoint2D(Point p) => new(p.X, p.Y);
}
