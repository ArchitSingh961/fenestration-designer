using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Fenestration.Designer.Interaction;
using Fenestration.Designer.Rendering;
using Fenestration.Designer.ViewModels;

namespace Fenestration.Designer.Controls;

/// <summary>
/// The 2D drawing surface. A lightweight <see cref="FrameworkElement"/> that hosts three
/// <see cref="DrawingVisual"/>s: the background (grid, axes), the content (the design) and the overlay
/// (selection box, handles, snap markers, previews). No WPF element is created per grid line or per object.
/// Redraws are coalesced to at most one per render pass, and each visual is only redrawn when its own
/// inputs changed, so a selection-box drag doesn't re-render the design.
///
/// Usage: <c>&lt;controls:ViewportControl Viewport="{Binding Canvas}" Tool="{Binding ActiveTool}" /&gt;</c>
/// </summary>
public sealed class ViewportControl : FrameworkElement
{
    public static readonly DependencyProperty ViewportProperty = DependencyProperty.Register(
        nameof(Viewport), typeof(CanvasViewModel), typeof(ViewportControl),
        new PropertyMetadata(null, OnViewportChanged));

    /// <summary>The editing tool that receives left-button and Esc/Delete/Ctrl+A input (optional).</summary>
    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool), typeof(IViewportTool), typeof(ViewportControl), new PropertyMetadata(null, OnToolChanged));

    /// <summary>Receives library designs dragged onto the view (optional).</summary>
    public static readonly DependencyProperty DropTargetProperty = DependencyProperty.Register(
        nameof(DropTarget), typeof(IViewportDropTarget), typeof(ViewportControl), new PropertyMetadata(null));

    private readonly DrawingVisual _backgroundVisual = new();
    private readonly DrawingVisual _contentVisual = new();
    private readonly DrawingVisual _overlayVisual = new();
    private readonly VisualCollection _visuals;
    private readonly ViewportRenderer _renderer = new();
    private readonly ViewportInteractionController _interaction;

    private bool _viewRedrawPending;
    private bool _contentRedrawPending;
    private bool _overlayRedrawPending;

    public ViewportControl()
    {
        _visuals = new VisualCollection(this) { _backgroundVisual, _contentVisual, _overlayVisual };
        _interaction = new ViewportInteractionController(this, () => Viewport, () => Tool);

        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
        AllowDrop = true;
    }

    public IViewportDropTarget? DropTarget
    {
        get => (IViewportDropTarget?)GetValue(DropTargetProperty);
        set => SetValue(DropTargetProperty, value);
    }

    // ── Drag and drop (library designs) ─────────────────────────────

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = DragDropEffects.None;
        if (DesignDrag(e) is { } drag && DropTarget!.DragOver(drag.World, drag.TemplateId))
            e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        DropTarget?.DragLeave();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (DesignDrag(e) is { } drag)
        {
            DropTarget!.Drop(drag.World, drag.TemplateId);
            Focus();
            e.Handled = true;
        }
        DropTarget?.DragLeave();
    }

    /// <summary>The dragged design and the world point under the mouse, or null if it isn't a design drag.</summary>
    private (Core.Geometry.Point2D World, string TemplateId)? DesignDrag(DragEventArgs e)
    {
        if (DropTarget is null || Viewport is not { } vm || !e.Data.GetDataPresent(IViewportDropTarget.DesignFormat)
            || e.Data.GetData(IViewportDropTarget.DesignFormat) is not string id)
            return null;
        var p = e.GetPosition(this);
        return (vm.Transform.ScreenToWorld(new Core.Geometry.Point2D(p.X, p.Y)), id);
    }

    public CanvasViewModel? Viewport
    {
        get => (CanvasViewModel?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public IViewportTool? Tool
    {
        get => (IViewportTool?)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    // ── Visual tree plumbing ────────────────────────────────────────

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    /// <summary>The whole surface is hit-testable, so mouse input works over empty background too.</summary>
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Viewport?.SetViewportSize(ActualWidth, ActualHeight);
        RequestViewRedraw();
    }

    // ── View model wiring ───────────────────────────────────────────

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ViewportControl)d;
        if (e.OldValue is CanvasViewModel oldVm)
        {
            oldVm.ViewChanged -= control.RequestViewRedraw;
            oldVm.ContentChanged -= control.RequestContentRedraw;
            oldVm.OverlayChanged -= control.RequestOverlayRedraw;
        }
        if (e.NewValue is CanvasViewModel newVm)
        {
            newVm.ViewChanged += control.RequestViewRedraw;
            newVm.ContentChanged += control.RequestContentRedraw;
            newVm.OverlayChanged += control.RequestOverlayRedraw;
            if (control.ActualWidth > 0)
                newVm.SetViewportSize(control.ActualWidth, control.ActualHeight);
        }
        control.RequestViewRedraw();
    }

    /// <summary>Switching tools abandons whatever the old tool was doing.</summary>
    private static void OnToolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ViewportControl)d;
        if (e.OldValue is IViewportTool { IsCapturing: true } oldTool)
            oldTool.Cancel();
        if (control.IsMouseCaptured)
            control.ReleaseMouseCapture();
        control.Cursor = Cursors.Cross;
    }

    // ── Redraw scheduling ───────────────────────────────────────────

    private void RequestViewRedraw()
    {
        _viewRedrawPending = true;
        ScheduleRedraw();
    }

    private void RequestContentRedraw()
    {
        _contentRedrawPending = true;
        ScheduleRedraw();
    }

    private void RequestOverlayRedraw()
    {
        _overlayRedrawPending = true;
        ScheduleRedraw();
    }

    private bool _redrawScheduled;

    /// <summary>
    /// Several changes in one input event (zoom sets zoom and pan, a drag updates preview and overlay, …)
    /// collapse into a single redraw at render priority.
    /// </summary>
    private void ScheduleRedraw()
    {
        if (_redrawScheduled) return;
        _redrawScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Redraw);
    }

    private void Redraw()
    {
        _redrawScheduled = false;
        var vm = Viewport;
        if (vm is null) return;

        var size = new Size(ActualWidth, ActualHeight);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (_viewRedrawPending)
        {
            using DrawingContext dc = _backgroundVisual.RenderOpen();
            var context = new ViewportDrawingContext(dc, vm.Transform, size, pixelsPerDip);
            _renderer.RenderBackground(context, new ViewportRenderOptions(vm.ShowGrid, vm.ShowAxes, vm.Settings));
        }

        // Content and overlay are in world space, so any view change moves them too.
        if (_viewRedrawPending || _contentRedrawPending)
        {
            using DrawingContext dc = _contentVisual.RenderOpen();
            var context = new ViewportDrawingContext(dc, vm.Transform, size, pixelsPerDip);
            _renderer.RenderContent(context, vm.ContentLayers);
        }

        if (_viewRedrawPending || _contentRedrawPending || _overlayRedrawPending)
        {
            using DrawingContext dc = _overlayVisual.RenderOpen();
            var context = new ViewportDrawingContext(dc, vm.Transform, size, pixelsPerDip);
            _renderer.RenderOverlay(context, vm.OverlayLayers);
        }

        _viewRedrawPending = false;
        _contentRedrawPending = false;
        _overlayRedrawPending = false;
    }
}
