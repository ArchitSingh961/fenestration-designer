using System.Globalization;
using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Designer.Interaction;
using Fenestration.Designer.ViewModels;

namespace Fenestration.Designer.Tools;

/// <summary>
/// The default tool: click to select a frame, mullion, transom or glass (Ctrl+click toggles), and drag a
/// mullion/transom to move it.
///
/// Everything is worked out in WORLD millimetres: the pointer arrives already converted by
/// <c>ScreenToWorld</c>, the hit tolerance is a pixel radius converted to mm at the current zoom, and a
/// drag moves the division by the world distance the mouse travelled. The drag updates the model live
/// through <see cref="FrameEditor"/> (always valid: it stops at the last valid position), and on release
/// records the whole drag as ONE undoable command.
/// </summary>
public sealed class SelectTool : IViewportTool
{
    public const double HitTolerancePixels = 5.0;
    public const double DragThresholdPixels = 3.0;
    public const double SnapTolerancePixels = 8.0;

    /// <summary>Drag positions are whole millimetres unless the snap grid is on.</summary>
    public const double DragIncrementMm = 1.0;

    private readonly MainViewModel _host;
    private DragState? _drag;

    public SelectTool(MainViewModel host) => _host = host;

    public bool IsCapturing => _drag is not null;

    public bool IsDragging => _drag?.Started == true;

    public void OnPointerDown(ViewportPointerEventArgs e)
    {
        e.Handled = true;
        var hit = HitTest(e.World);

        if (hit is not { } h)
        {
            if (!e.IsControlPressed) _host.ClearSelection();
            return;
        }

        if (e.IsControlPressed)
        {
            if (_host.IsSelected(h.ElementId)) _host.Deselect(h.ElementId);
            else _host.Select(h.ElementId, addToSelection: true);
            return;
        }

        _host.Select(h.ElementId);

        if (h.Kind is DesignElementKind.Mullion or DesignElementKind.Transom
            && _host.Project.Frames.FirstOrDefault(f => f.Id == h.FrameId) is { } frame
            && frame.Profiles.FirstOrDefault(p => p.Id == h.ElementId) is { } division)
        {
            double position = Members.DivisionPosition(division);
            _drag = new DragState(frame, division.Id, h.Kind, e.Screen, e.World, position, FrameSnapshot.Capture(frame))
            {
                LastValidPosition = position
            };
            e.Cursor = CursorFor(h.Kind);
        }
    }

    public void OnPointerMove(ViewportPointerEventArgs e)
    {
        if (_drag is not { } drag)
        {
            e.Cursor = HitTest(e.World) is { } hover ? CursorFor(hover.Kind) : ViewportCursor.Default;
            return;
        }

        e.Handled = true;
        e.Cursor = CursorFor(drag.Kind);

        if (!drag.Started)
        {
            if (drag.StartScreen.DistanceTo(e.Screen) < DragThresholdPixels) return;
            drag.Started = true;
        }

        bool vertical = drag.Kind == DesignElementKind.Mullion;
        double delta = vertical ? e.World.X - drag.StartWorld.X : e.World.Y - drag.StartWorld.Y;

        double snapTolerance = _host.Canvas.ScreenToWorldDistance(SnapTolerancePixels);
        double increment = _host.Canvas.SnapToGrid ? _host.Canvas.GridSpacingMm : DragIncrementMm;
        var snap = DivisionSnapper.Snap(drag.Frame, drag.DivisionId, drag.StartPosition + delta, snapTolerance, increment);

        double position = ClampToValid(drag, snap.Position);
        if (position != drag.LastValidPosition)
        {
            FrameEditor.MoveDivision(drag.Frame, drag.DivisionId, position, _host.Rules);
            drag.LastValidPosition = position;
            _host.OnDesignChanged();
        }

        string name = vertical ? "Mullion" : "Transom";
        string snapNote = snap.Kind switch
        {
            DivisionSnapKind.FrameCenter => "  (frame centre)",
            DivisionSnapKind.BayCenter => "  (equal split)",
            DivisionSnapKind.AlignedDivision => "  (aligned)",
            _ => ""
        };
        _host.Hint = string.Create(CultureInfo.InvariantCulture, $"{name} at {drag.LastValidPosition:0.#} mm{snapNote}");
    }

    public void OnPointerUp(ViewportPointerEventArgs e)
    {
        if (_drag is not { } drag) return;
        e.Handled = true;
        _drag = null;

        if (drag.Started && drag.LastValidPosition != drag.StartPosition)
        {
            string name = drag.Kind == DesignElementKind.Mullion ? "mullion" : "transom";
            _host.CommandHistory.Execute(FrameEditCommand.FromCompletedEdit(
                $"Move {name} to {drag.LastValidPosition:0.#} mm", drag.Frame, drag.Before, FrameSnapshot.Capture(drag.Frame)));
        }
        _host.Hint = null;
    }

    public bool OnKey(ViewportKey key)
    {
        switch (key)
        {
            case ViewportKey.Escape when _drag is { } drag:
                // Cancel the drag: put everything back exactly as it was.
                if (drag.Started)
                {
                    drag.Before.ApplyTo(drag.Frame);
                    _host.OnDesignChanged();
                }
                _drag = null;
                _host.Hint = null;
                return true;

            case ViewportKey.Escape:
                _host.ClearSelection();
                return true;

            case ViewportKey.Delete when _drag is null:
                _host.DeleteSelection();
                return true;

            default:
                return false;
        }
    }

    private DesignHit? HitTest(Point2D world)
        => FrameHitTester.HitTest(_host.Project, world, _host.Canvas.ScreenToWorldDistance(HitTolerancePixels));

    /// <summary>
    /// The target if it's valid, otherwise the valid position closest to it on the way from the last valid
    /// position (binary search in whole mm). The division stops at the limit instead of refusing to move.
    /// </summary>
    private double ClampToValid(DragState drag, double target)
    {
        if (FrameEditor.CanMoveDivision(drag.Frame, drag.DivisionId, target, _host.Rules))
            return target;

        double valid = drag.LastValidPosition, invalid = target;
        while (Math.Abs(invalid - valid) > DragIncrementMm)
        {
            double mid = Math.Round((valid + invalid) / 2.0);
            if (mid == valid || mid == invalid) break;
            if (FrameEditor.CanMoveDivision(drag.Frame, drag.DivisionId, mid, _host.Rules)) valid = mid;
            else invalid = mid;
        }
        return valid;
    }

    private static ViewportCursor CursorFor(DesignElementKind kind) => kind switch
    {
        DesignElementKind.Mullion => ViewportCursor.ResizeHorizontal,
        DesignElementKind.Transom => ViewportCursor.ResizeVertical,
        _ => ViewportCursor.Default
    };

    private sealed record DragState(
        Frame Frame,
        Guid DivisionId,
        DesignElementKind Kind,
        Point2D StartScreen,
        Point2D StartWorld,
        double StartPosition,
        FrameSnapshot Before)
    {
        public bool Started { get; set; }
        public double LastValidPosition { get; set; }
    }
}
