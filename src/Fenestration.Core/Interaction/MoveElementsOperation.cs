using System.Globalization;
using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;
using Fenestration.Core.Snapping;

namespace Fenestration.Core.Interaction;

/// <summary>
/// Dragging the selection: whole frames move freely; mullions move in X and transoms in Y.
///
/// Transaction: <c>Begin</c> (mouse down) → many <see cref="Update"/>s (mouse moves: snap → validate → preview)
/// → <see cref="CreateCommand"/> (mouse up) produces ONE undoable command for the whole drag, a composite when
/// several frames are involved. The model is never touched before that, so cancelling is simply
/// discarding the operation.
///
/// Snapping follows the grabbed object: a grabbed division snaps its position along its axis; a grabbed frame
/// snaps its nearest corner. Other selected objects move by the same offset. If the candidate is invalid, the
/// preview stops at the last valid offset (found by <see cref="DragClamp"/>) and shows the invalid candidate as
/// a ghost with the reason.
/// </summary>
public sealed class MoveElementsOperation
{
    private sealed record DivisionStart(Guid Id, MemberAxis Axis, double Start, Rectangle2D WorldBody);

    private readonly DesignRules _rules;
    private readonly SnapSettings _settings;
    private readonly SnapSession _snap;
    private readonly List<Frame> _frames;
    private readonly List<(Frame Frame, List<DivisionStart> Divisions)> _groups = new();
    private readonly Frame? _grabbedFrame;
    private readonly (Frame Frame, DivisionStart Division)? _grabbedDivision;
    private readonly Point2D _grabPoint;

    public MoveElementsOperation(Project project, IEnumerable<Guid> selectedIds, Guid grabbedId, Point2D grabWorldPoint,
        DesignRules rules, SnapEngine snapEngine)
    {
        ArgumentNullException.ThrowIfNull(project);
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _settings = snapEngine.Settings;
        _grabPoint = grabWorldPoint;

        var ids = selectedIds.ToHashSet();
        ids.Add(grabbedId);

        // A selected frame carries its children, so its own selected divisions don't move separately.
        _frames = project.Frames.Where(f => ids.Contains(f.Id)).ToList();
        foreach (var frame in project.Frames.Except(_frames))
        {
            var offset = new Vector2D(frame.X, frame.Y);
            var divisions = frame.Profiles
                .Where(p => Members.IsDivision(p) && ids.Contains(p.Id))
                .Select(p => new DivisionStart(p.Id, p.ProfileType == ProfileType.Mullion ? MemberAxis.Vertical : MemberAxis.Horizontal,
                    Members.DivisionPosition(p), FrameLayout.GetMemberBody(frame, p).Offset(offset)))
                .ToList();
            if (divisions.Count > 0)
                _groups.Add((frame, divisions));
        }

        _grabbedFrame = _frames.FirstOrDefault(f => f.Id == grabbedId || f.Profiles.Any(p => p.Id == grabbedId));
        if (_grabbedFrame is null)
        {
            foreach (var (frame, divisions) in _groups)
                if (divisions.FirstOrDefault(d => d.Id == grabbedId) is { } grabbed)
                    _grabbedDivision = (frame, grabbed);
        }
        _grabbedFrame ??= _grabbedDivision is null ? _frames.FirstOrDefault() : null;

        var moving = _frames.Select(f => f.Id).Concat(_groups.SelectMany(g => g.Divisions.Select(d => d.Id)));
        _snap = snapEngine.BeginSession(project, moving);
    }

    /// <summary>True if nothing in the selection can move (e.g. only glass is selected).</summary>
    public bool IsEmpty => _frames.Count == 0 && _groups.Count == 0;

    /// <summary>The last VALID offset reached; this is what <see cref="CreateCommand"/> commits.</summary>
    public Vector2D CommittedOffset { get; private set; } = Vector2D.Zero;

    public OperationPreview Update(Point2D currentWorldPoint, double snapToleranceMm)
    {
        if (IsEmpty) return OperationPreview.Empty;

        var (offset, indicator) = SnapOffset(currentWorldPoint - _grabPoint, currentWorldPoint, snapToleranceMm);

        if (TryBuild(offset, out var frames, out string? error))
        {
            CommittedOffset = offset;
            return new OperationPreview(frames, Array.Empty<Rectangle2D>(), true, Describe(offset), indicator);
        }

        CommittedOffset = DragClamp.Clamp(CommittedOffset, offset, o => TryBuild(o, out _, out _), _settings.RoundingIncrementMm);
        TryBuild(CommittedOffset, out var clampedFrames, out _);
        return new OperationPreview(clampedFrames, Ghosts(offset), false, error, indicator);
    }

    /// <summary>The single undoable command for the whole drag, or null if nothing moved.</summary>
    public IUndoableCommand? CreateCommand()
    {
        var offset = CommittedOffset;
        if (offset.LengthSquared <= GeometryTolerance.Epsilon) return null;

        var commands = new List<IUndoableCommand>();
        foreach (var frame in _frames)
            commands.Add(new MoveFrameCommand(frame, frame.X + offset.X, frame.Y + offset.Y));
        foreach (var (frame, divisions) in _groups)
        {
            var moves = Moves(divisions, offset);
            if (moves.Count > 0)
                commands.Add(new MoveDivisionsCommand(frame, moves, _rules));
        }

        int count = _frames.Count + _groups.Sum(g => g.Divisions.Count);
        return CompositeCommand.Combine($"Move {count} objects", commands);
    }

    // ── Internals ───────────────────────────────────────────────────

    private (Vector2D Offset, SnapIndicator? Indicator) SnapOffset(Vector2D raw, Point2D pointer, double tolerance)
    {
        double increment = _settings.RoundingIncrementMm;

        if (_grabbedDivision is { } grabbed)
        {
            var (frame, division) = grabbed;
            bool vertical = division.Axis == MemberAxis.Vertical;
            double origin = vertical ? frame.X : frame.Y;
            double world = origin + division.Start + (vertical ? raw.X : raw.Y);
            var snap = _snap.SnapCoordinate(vertical ? SnapAxis.X : SnapAxis.Y, world, tolerance);

            double along = snap.IsSnapped
                ? snap.Value - origin - division.Start
                : DragClamp.Round(division.Start + (vertical ? raw.X : raw.Y), increment) - division.Start;
            double across = DragClamp.Round(vertical ? raw.Y : raw.X, increment);

            SnapIndicator? indicator = null;
            if (snap.IsSnapped)
            {
                var alignFrom = vertical ? new Point2D(snap.Value, pointer.Y) : new Point2D(pointer.X, snap.Value);
                indicator = new SnapIndicator(snap.Target ?? alignFrom, snap.Type, alignFrom);
            }
            return (vertical ? new Vector2D(along, across) : new Vector2D(across, along), indicator);
        }

        if (_grabbedFrame is { } grabbedFrame)
        {
            // Try each corner; the best snap (priority, then distance) decides the offset.
            SnapResult? best = null;
            Point2D bestCorner = default;
            var bounds = grabbedFrame.Bounds;
            foreach (var corner in new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft })
            {
                var result = _snap.SnapPoint(corner + raw, tolerance);
                if (result.Type is SnapType.None or SnapType.Grid) continue;
                if (best is null || Rank(result.Type) < Rank(best.Type) || (result.Type == best.Type && result.Distance < best.Distance))
                    (best, bestCorner) = (result, corner);
            }

            if (best is not null)
                return (best.Point - bestCorner, new SnapIndicator(best.Point, best.Type));

            if (_settings.GridEnabled)
            {
                var gridCorner = GridSnapProvider.Snap(bounds.TopLeft + raw, _settings.GridSpacingMm);
                return (gridCorner - bounds.TopLeft, new SnapIndicator(gridCorner, SnapType.Grid));
            }
        }

        return (DragClamp.Round(raw, increment), null);
    }

    private int Rank(SnapType type)
    {
        int index = _settings.Priority.ToList().IndexOf(type);
        return index < 0 ? int.MaxValue : index;
    }

    private bool TryBuild(Vector2D offset, out Dictionary<Guid, Frame> frames, out string? error)
    {
        frames = new Dictionary<Guid, Frame>();
        error = null;

        foreach (var frame in _frames)
        {
            var copy = FrameSnapshot.Capture(frame).ToFrame();
            copy.X += offset.X;
            copy.Y += offset.Y;
            frames[frame.Id] = copy;
        }

        foreach (var (frame, divisions) in _groups)
        {
            var moves = Moves(divisions, offset);
            if (moves.Count == 0) continue;
            var copy = FrameSnapshot.Capture(frame).ToFrame();
            var result = FrameEditor.TryMoveDivisions(copy, moves, _rules);
            if (!result.Success)
            {
                error = result.Error;
                return false;
            }
            frames[frame.Id] = copy;
        }
        return true;
    }

    private static List<DivisionMove> Moves(List<DivisionStart> divisions, Vector2D offset)
        => divisions
            .Select(d => (d, delta: d.Axis == MemberAxis.Vertical ? offset.X : offset.Y))
            .Where(x => Math.Abs(x.delta) > GeometryTolerance.Epsilon)
            .Select(x => new DivisionMove(x.d.Id, x.d.Start + x.delta))
            .ToList();

    private List<Rectangle2D> Ghosts(Vector2D offset)
    {
        var ghosts = _frames.Select(f => f.Bounds.Offset(offset)).ToList();
        foreach (var (_, divisions) in _groups)
            foreach (var d in divisions)
                ghosts.Add(d.WorldBody.Offset(d.Axis == MemberAxis.Vertical ? new Vector2D(offset.X, 0) : new Vector2D(0, offset.Y)));
        return ghosts;
    }

    private string Describe(Vector2D offset)
    {
        if (_frames.Count == 0 && _groups.Count == 1 && _groups[0].Divisions.Count == 1)
        {
            var d = _groups[0].Divisions[0];
            double position = d.Start + (d.Axis == MemberAxis.Vertical ? offset.X : offset.Y);
            return string.Create(CultureInfo.InvariantCulture,
                $"{(d.Axis == MemberAxis.Vertical ? "Mullion" : "Transom")} at {position:0.#} mm");
        }
        if (_frames.Count == 1 && _groups.Count == 0)
            return string.Create(CultureInfo.InvariantCulture, $"Frame at ({_frames[0].X + offset.X:0.#}, {_frames[0].Y + offset.Y:0.#}) mm");

        int count = _frames.Count + _groups.Sum(g => g.Divisions.Count);
        return string.Create(CultureInfo.InvariantCulture, $"Moving {count} objects by ({offset.X:0.#}, {offset.Y:0.#}) mm");
    }
}
