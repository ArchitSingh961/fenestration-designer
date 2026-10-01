using System.Globalization;
using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;
using Fenestration.Core.Snapping;

namespace Fenestration.Core.Interaction;

/// <summary>
/// Drawing a new frame as a rectangle: both corners snap (geometry, then grid, else whole mm); the size is
/// validated exactly as typed sizes are (<see cref="FrameEditor.TryCreateFrame"/>). The preview is a ghost
/// rectangle; release creates one <see cref="CreateFrameCommand"/>, or nothing if the size is invalid.
/// </summary>
public sealed class CreateFrameOperation
{
    private readonly Project _project;
    private readonly DesignRules _rules;
    private readonly SnapSettings _settings;
    private readonly SnapSession _snap;
    private readonly Point2D _start;
    private Frame? _candidate;

    public CreateFrameOperation(Project project, Point2D startWorldPoint, double snapToleranceMm, DesignRules rules, SnapEngine snapEngine)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _settings = snapEngine.Settings;
        _snap = snapEngine.BeginSession(project);
        _start = SnapOrRound(startWorldPoint, snapToleranceMm, out _);
    }

    public Point2D Start => _start;

    public OperationPreview Update(Point2D currentWorldPoint, double snapToleranceMm)
    {
        var end = SnapOrRound(currentWorldPoint, snapToleranceMm, out var indicator);
        var rect = Rectangle2D.FromCorners(_start, end);

        var result = FrameEditor.TryCreateFrame(rect.Left, rect.Top, rect.Width, rect.Height, _rules, out _candidate);
        string message = result.Success
            ? string.Create(CultureInfo.InvariantCulture, $"New frame {rect.Width:0.#} × {rect.Height:0.#} mm")
            : result.Error!;
        return new OperationPreview(new Dictionary<Guid, Frame>(), new[] { rect }, result.Success, message, indicator);
    }

    /// <summary>The frame to create, or null if the last candidate was invalid.</summary>
    public IUndoableCommand? CreateCommand() => _candidate is null ? null : new CreateFrameCommand(_project, _candidate);

    private Point2D SnapOrRound(Point2D point, double tolerance, out SnapIndicator? indicator)
    {
        var snap = _snap.SnapPoint(point, tolerance);
        if (snap.Type != SnapType.None)
        {
            indicator = new SnapIndicator(snap.Point, snap.Type);
            return snap.Point;
        }
        indicator = null;
        return new Point2D(DragClamp.Round(point.X, _settings.RoundingIncrementMm), DragClamp.Round(point.Y, _settings.RoundingIncrementMm));
    }
}

/// <summary>
/// Placing a mullion (<see cref="MemberAxis.Vertical"/>) or transom (<see cref="MemberAxis.Horizontal"/>) with the
/// pointer. Hovering over a glass panel previews a division through that opening at the (snapped) pointer position;
/// with <c>wholeFrame</c> it spans the whole frame instead. The preview is the fully re-derived frame, or an
/// invalid ghost with the reason. Click commits one <see cref="AddDivisionCommand"/>.
/// </summary>
public sealed class AddDivisionOperation
{
    private readonly Project _project;
    private readonly MemberAxis _axis;
    private readonly DesignRules _rules;
    private readonly SnapSettings _settings;
    private readonly SnapSession _snap;
    private (Frame Frame, Guid? GlassId, double Position)? _candidate;

    public AddDivisionOperation(Project project, MemberAxis axis, DesignRules rules, SnapEngine snapEngine)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _axis = axis;
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _settings = snapEngine.Settings;
        _snap = snapEngine.BeginSession(project);
    }

    public MemberAxis Axis => _axis;

    public bool HasValidCandidate => _candidate is not null;

    public OperationPreview Update(Point2D worldPoint, double snapToleranceMm, bool wholeFrame)
    {
        _candidate = null;
        string name = _axis == MemberAxis.Vertical ? "mullion" : "transom";
        var hit = FrameHitTester.HitTest(_project, worldPoint, 0);
        var frame = hit is { } h ? _project.Frames.FirstOrDefault(f => f.Id == h.FrameId) : null;
        Guid? glassId = hit is { Kind: DesignElementKind.Glass } g ? g.ElementId : null;

        if (frame is null || (glassId is null && !wholeFrame))
            return new OperationPreview(new Dictionary<Guid, Frame>(), Array.Empty<Rectangle2D>(), false,
                $"Point at a glass panel to add a {name} (hold Shift to span the whole frame).", null);

        bool vertical = _axis == MemberAxis.Vertical;
        double origin = vertical ? frame.X : frame.Y;
        var snap = _snap.SnapCoordinate(vertical ? SnapAxis.X : SnapAxis.Y, vertical ? worldPoint.X : worldPoint.Y, snapToleranceMm);
        double position = snap.IsSnapped
            ? snap.Value - origin
            : DragClamp.Round((vertical ? worldPoint.X : worldPoint.Y) - origin, _settings.RoundingIncrementMm);

        SnapIndicator? indicator = null;
        if (snap.IsSnapped)
        {
            var alignFrom = vertical ? new Point2D(snap.Value, worldPoint.Y) : new Point2D(worldPoint.X, snap.Value);
            indicator = new SnapIndicator(snap.Target ?? alignFrom, snap.Type, alignFrom);
        }

        Guid? split = wholeFrame ? null : glassId;
        var copy = FrameSnapshot.Capture(frame).ToFrame();
        var result = FrameEditor.TryAddDivision(copy, _axis, split, position, _rules, out _);
        string label = string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(name[0])}{name[1..]} at {position:0.#} mm");

        if (result.Success)
        {
            _candidate = (frame, split, position);
            return new OperationPreview(new Dictionary<Guid, Frame> { [frame.Id] = copy }, Array.Empty<Rectangle2D>(), true, label, indicator);
        }

        var ghosts = new List<Rectangle2D>();
        if (FrameEditor.TryGetDivisionArea(frame, split, _rules, out var area) is null)
        {
            double half = (vertical ? _rules.MullionThicknessMm : _rules.TransomThicknessMm) / 2.0;
            var body = vertical
                ? Rectangle2D.FromCorners(new Point2D(position - half, area.Top), new Point2D(position + half, area.Bottom))
                : Rectangle2D.FromCorners(new Point2D(area.Left, position - half), new Point2D(area.Right, position + half));
            ghosts.Add(body.Offset(new Vector2D(frame.X, frame.Y)));
        }
        return new OperationPreview(new Dictionary<Guid, Frame>(), ghosts, false, result.Error, indicator);
    }

    /// <summary>The command for the last valid candidate, or null.</summary>
    public IUndoableCommand? CreateCommand()
        => _candidate is { } c ? new AddDivisionCommand(c.Frame, _axis, c.GlassId, c.Position, _rules) : null;
}
