using System.Globalization;
using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Core.Snapping;

namespace Fenestration.Core.Interaction;

/// <summary>
/// Dragging a frame's resize handle: mouse → candidate width/height → snap the moving edge →
/// validate the frame and its divisions (<see cref="FrameEditor.TryResize"/>) → preview → one
/// <see cref="ResizeFrameCommand"/> on release. A size that would invalidate existing divisions is rejected
/// (the preview stops at the last valid size); divisions are never moved to make room.
/// </summary>
public sealed class ResizeFrameOperation
{
    private readonly Frame _frame;
    private readonly FrameHandle _handle;
    private readonly DesignRules _rules;
    private readonly SnapSettings _settings;
    private readonly SnapSession _snap;
    private readonly Point2D _grabPoint;
    private readonly double _startWidth;
    private readonly double _startHeight;

    public ResizeFrameOperation(Project project, Frame frame, FrameHandle handle, Point2D grabWorldPoint, DesignRules rules, SnapEngine snapEngine)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _handle = handle;
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _settings = snapEngine.Settings;
        _grabPoint = grabWorldPoint;
        _startWidth = frame.Width;
        _startHeight = frame.Height;
        _snap = snapEngine.BeginSession(project, new[] { frame.Id });
    }

    public Frame Frame => _frame;

    /// <summary>The last valid size, as the change (Δwidth, Δheight) from the starting size.</summary>
    public Vector2D CommittedChange { get; private set; } = Vector2D.Zero;

    public OperationPreview Update(Point2D currentWorldPoint, double snapToleranceMm)
    {
        var raw = currentWorldPoint - _grabPoint;
        double increment = _settings.RoundingIncrementMm;
        double dw = 0, dh = 0;
        SnapIndicator? indicator = null;

        if (FrameHandles.ChangesWidth(_handle))
        {
            var snap = _snap.SnapCoordinate(SnapAxis.X, _frame.X + _startWidth + raw.X, snapToleranceMm);
            dw = snap.IsSnapped ? snap.Value - _frame.X - _startWidth : DragClamp.Round(_startWidth + raw.X, increment) - _startWidth;
            if (snap.IsSnapped)
                indicator = new SnapIndicator(snap.Target ?? new Point2D(snap.Value, currentWorldPoint.Y), snap.Type,
                    new Point2D(snap.Value, currentWorldPoint.Y));
        }
        if (FrameHandles.ChangesHeight(_handle))
        {
            var snap = _snap.SnapCoordinate(SnapAxis.Y, _frame.Y + _startHeight + raw.Y, snapToleranceMm);
            dh = snap.IsSnapped ? snap.Value - _frame.Y - _startHeight : DragClamp.Round(_startHeight + raw.Y, increment) - _startHeight;
            if (snap.IsSnapped && indicator is null)
                indicator = new SnapIndicator(snap.Target ?? new Point2D(currentWorldPoint.X, snap.Value), snap.Type,
                    new Point2D(currentWorldPoint.X, snap.Value));
        }

        var change = new Vector2D(dw, dh);
        if (TryBuild(change, out var preview, out string? error))
        {
            CommittedChange = change;
            return new OperationPreview(Replacement(preview!), Array.Empty<Rectangle2D>(), true, Describe(change), indicator);
        }

        CommittedChange = DragClamp.Clamp(CommittedChange, change, c => TryBuild(c, out _, out _), increment);
        TryBuild(CommittedChange, out var clamped, out _);

        double w = _startWidth + dw, h = _startHeight + dh;
        var ghosts = w > 0 && h > 0
            ? new[] { new Rectangle2D(_frame.X, _frame.Y, w, h) }
            : Array.Empty<Rectangle2D>();
        return new OperationPreview(clamped is null ? new Dictionary<Guid, Frame>() : Replacement(clamped), ghosts, false, error, indicator);
    }

    /// <summary>One <see cref="ResizeFrameCommand"/>, or null if the size didn't change.</summary>
    public IUndoableCommand? CreateCommand()
        => CommittedChange.LengthSquared <= GeometryTolerance.Epsilon
            ? null
            : new ResizeFrameCommand(_frame, _startWidth + CommittedChange.X, _startHeight + CommittedChange.Y, _rules);

    private bool TryBuild(Vector2D change, out Frame? preview, out string? error)
    {
        var copy = FrameSnapshot.Capture(_frame).ToFrame();
        var result = FrameEditor.TryResize(copy, _startWidth + change.X, _startHeight + change.Y, _rules);
        preview = result.Success ? copy : null;
        error = result.Error;
        return result.Success;
    }

    private Dictionary<Guid, Frame> Replacement(Frame preview) => new() { [_frame.Id] = preview };

    private string Describe(Vector2D change) => string.Create(CultureInfo.InvariantCulture,
        $"Frame {_startWidth + change.X:0.#} × {_startHeight + change.Y:0.#} mm");
}
