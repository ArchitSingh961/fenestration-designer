using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Core.Utilities;

namespace Fenestration.Core.Design;

/// <summary>Outcome of a validated edit: success, or the user-facing reason it was rejected.</summary>
public readonly record struct EditResult(bool Success, string? Error)
{
    public static EditResult Ok => new(true, null);

    public static EditResult Fail(string error) => new(false, error);

    /// <exception cref="DesignValidationException">The edit failed.</exception>
    public void ThrowIfFailed()
    {
        if (!Success)
            throw new DesignValidationException(Error ?? "The edit is not valid.");
    }
}

/// <summary>A requested new position (frame-relative mm) for one mullion or transom.</summary>
public readonly record struct DivisionMove(Guid DivisionId, double Position);

/// <summary>
/// The frame-designer operations: create, resize, move, add/move/delete divisions. Every operation is ATOMIC:
/// it runs on a copy of the frame, validates the result with <see cref="FrameLayout"/>, re-derives the glass,
/// and only then writes the result back.
///
/// Two flavours of each edit:
/// <list type="bullet">
///   <item><c>TryXxx</c> returns an <see cref="EditResult"/>. It is cheap enough to call on every mouse move
///         (e.g. on a preview copy while dragging).</item>
///   <item><c>Xxx</c> throws <see cref="DesignValidationException"/> on failure (used by commands).</item>
/// </list>
/// Either way, a failed edit leaves the frame untouched.
/// All positions are frame-relative millimetres (0 = the frame's outer left/top edge).
/// </summary>
public static class FrameEditor
{
    private const double Tol = GeometryTolerance.Default;

    // ── Frame ───────────────────────────────────────────────────────

    /// <summary>Creates a rectangular frame with four outer profiles and one derived glass panel.</summary>
    public static Frame CreateFrame(double x, double y, double width, double height, DesignRules rules)
    {
        TryCreateFrame(x, y, width, height, rules, out var frame).ThrowIfFailed();
        return frame!;
    }

    public static EditResult TryCreateFrame(double x, double y, double width, double height, DesignRules rules, out Frame? frame)
    {
        ArgumentNullException.ThrowIfNull(rules);
        frame = null;
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return EditResult.Fail("The frame position must be a finite number.");
        if (CheckFrameSize(width, height, rules.FrameThicknessMm, rules.FrameThicknessMm, rules) is { } sizeError)
            return EditResult.Fail(sizeError);

        var created = new Frame { X = x, Y = y, Width = width, Height = height };
        created.Profiles.AddRange(BuildOuterProfiles(width, height, rules.FrameThicknessMm));

        var layout = FrameLayout.Compute(created, rules);
        if (!layout.IsValid)
            return EditResult.Fail(layout.Errors[0]);
        RegenerateGlass(created, layout, rules);
        frame = created;
        return EditResult.Ok;
    }

    /// <summary>
    /// Changes the outer size. The left and top edges stay put; the right and bottom frame members move,
    /// dragging with them every division that ends on them. Divisions keep their positions, so shrinking
    /// the frame past a division is rejected (existing structure is never silently moved).
    /// </summary>
    public static void Resize(Frame frame, double width, double height, DesignRules rules)
        => TryResize(frame, width, height, rules).ThrowIfFailed();

    public static EditResult TryResize(Frame frame, double width, double height, DesignRules rules)
    {
        var outer = FrameMembers.Find(frame);
        if (outer is null)
            return EditResult.Fail("The frame has no outer profiles to resize.");
        if (CheckFrameSize(width, height, Math.Max(outer.Left.Thickness, outer.Right.Thickness),
                Math.Max(outer.Top.Thickness, outer.Bottom.Thickness), rules) is { } sizeError)
            return EditResult.Fail(sizeError);

        return TryCommit(frame, rules, "Cannot resize the frame: ", scratch =>
        {
            var members = FrameMembers.Find(scratch)!;
            MoveMember(scratch, members.Right, width - members.Right.Thickness / 2.0);
            MoveMember(scratch, members.Bottom, height - members.Bottom.Thickness / 2.0);
            scratch.Width = width;
            scratch.Height = height;
            return null;
        });
    }

    /// <summary>Moves the whole frame (its outer top-left corner) to a new world position. Children move with it.</summary>
    public static void MoveFrame(Frame frame, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new DesignValidationException("The frame position must be a finite number.");
        frame.X = x;
        frame.Y = y;
    }

    // ── Divisions ───────────────────────────────────────────────────

    /// <summary>
    /// Adds a mullion (<see cref="MemberAxis.Vertical"/>) or transom (<see cref="MemberAxis.Horizontal"/>).
    /// </summary>
    /// <param name="splitGlassId">
    /// Split only this glass opening, so the division runs between that opening's surrounding members.
    /// If null, the division runs across the whole frame opening, crossing existing perpendicular divisions.
    /// </param>
    /// <param name="position">Frame-relative position in mm; null places it in the middle of the widest free space.</param>
    /// <returns>The new profile's Id.</returns>
    public static Guid AddDivision(Frame frame, MemberAxis axis, Guid? splitGlassId, double? position, DesignRules rules)
    {
        TryAddDivision(frame, axis, splitGlassId, position, rules, out var id).ThrowIfFailed();
        return id;
    }

    public static EditResult TryAddDivision(Frame frame, MemberAxis axis, Guid? splitGlassId, double? position,
        DesignRules rules, out Guid newProfileId)
    {
        newProfileId = Guid.Empty;
        if (TryGetDivisionArea(frame, splitGlassId, rules, out var area) is { } areaError)
            return EditResult.Fail(areaError);

        double lower = axis == MemberAxis.Vertical ? area.Left : area.Top;
        double upper = axis == MemberAxis.Vertical ? area.Right : area.Bottom;
        double pos = position ?? DefaultPosition(frame, axis, area, wholeOpening: splitGlassId is null);
        if (!double.IsFinite(pos) || pos <= lower + Tol || pos >= upper - Tol)
            return EditResult.Fail($"The position must be between {Members.Format(lower)} and {Members.Format(upper)} mm.");

        var profile = axis == MemberAxis.Vertical
            ? new Profile
            {
                ProfileType = ProfileType.Mullion,
                StartPoint = new Point2D(pos, area.Top),
                EndPoint = new Point2D(pos, area.Bottom),
                Thickness = rules.MullionThicknessMm
            }
            : new Profile
            {
                ProfileType = ProfileType.Transom,
                StartPoint = new Point2D(area.Left, pos),
                EndPoint = new Point2D(area.Right, pos),
                Thickness = rules.TransomThicknessMm
            };

        var result = TryCommit(frame, rules, "Cannot add the division: ", scratch =>
        {
            scratch.Profiles.Add(profile);
            return null;
        });
        if (result.Success) newProfileId = profile.Id;
        return result;
    }

    /// <summary>
    /// The centreline rectangle a new division would span: the selected glass opening, or the whole frame opening.
    /// Returns an error message, or null on success.
    /// </summary>
    public static string? TryGetDivisionArea(Frame frame, Guid? splitGlassId, DesignRules rules, out Rectangle2D area)
    {
        area = default;
        var outer = FrameMembers.Find(frame);
        if (outer is null) return "The frame has no outer profiles.";
        var current = FrameLayout.Compute(frame, rules);
        if (!current.IsValid) return current.Errors[0];

        area = outer.CenterlineLoop;
        if (splitGlassId is not { } glassId) return null;

        var glass = frame.GlassPanels.FirstOrDefault(g => g.Id == glassId);
        if (glass is null) return "The selected glass panel is not part of this frame.";
        if (current.FindByGlass(glass.Boundary) is not { } region) return "The selected glass panel no longer matches the frame layout.";
        area = region.CenterlineBounds;
        return null;
    }

    /// <summary>Moves a mullion (new X) or transom (new Y). Divisions ending on it move their ends with it.</summary>
    public static void MoveDivision(Frame frame, Guid divisionId, double position, DesignRules rules)
        => TryMoveDivisions(frame, new[] { new DivisionMove(divisionId, position) }, rules).ThrowIfFailed();

    /// <summary>Moves several divisions of one frame as a single atomic edit (validated once, as a whole).</summary>
    public static void MoveDivisions(Frame frame, IReadOnlyList<DivisionMove> moves, DesignRules rules)
        => TryMoveDivisions(frame, moves, rules).ThrowIfFailed();

    public static EditResult TryMoveDivisions(Frame frame, IReadOnlyList<DivisionMove> moves, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(moves);
        var pending = new List<DivisionMove>();
        Profile? single = null;
        foreach (var move in moves)
        {
            if (FindDivision(frame, move.DivisionId, out var division) is { } findError)
                return EditResult.Fail(findError);
            if (!double.IsFinite(move.Position))
                return EditResult.Fail("The position must be a number.");
            if (Math.Abs(Members.DivisionPosition(division!) - move.Position) > GeometryTolerance.Epsilon)
                pending.Add(move);
            single = division;
        }
        if (pending.Count == 0)
            return EditResult.Ok;

        string prefix = moves.Count == 1 && single is not null
            ? $"Cannot move {Members.Describe(single).ToLowerInvariant()}: "
            : "Cannot move the divisions: ";

        return TryCommit(frame, rules, prefix, scratch =>
        {
            foreach (var move in pending)
                if (MoveMember(scratch, scratch.Profiles.First(p => p.Id == move.DivisionId), move.Position) is { } error)
                    return error;
            return null;
        });
    }

    /// <summary>True if <see cref="MoveDivision"/> would succeed. Nothing is modified.</summary>
    public static bool CanMoveDivision(Frame frame, Guid divisionId, double position, DesignRules rules)
        => TryMoveDivisions(FrameSnapshot.Capture(frame).ToFrame(), new[] { new DivisionMove(divisionId, position) }, rules).Success;

    /// <summary>Removes a mullion or transom. Rejected while other divisions end on it (they would be left hanging).</summary>
    public static void DeleteDivision(Frame frame, Guid divisionId, DesignRules rules)
        => TryDeleteDivisions(frame, new[] { divisionId }, rules).ThrowIfFailed();

    /// <summary>Removes several divisions of one frame at once. Divisions ending on a removed one must be removed too.</summary>
    public static void DeleteDivisions(Frame frame, IReadOnlyCollection<Guid> divisionIds, DesignRules rules)
        => TryDeleteDivisions(frame, divisionIds, rules).ThrowIfFailed();

    public static EditResult TryDeleteDivisions(Frame frame, IReadOnlyCollection<Guid> divisionIds, DesignRules rules)
    {
        var ids = divisionIds.ToHashSet();
        foreach (var id in ids)
        {
            if (FindDivision(frame, id, out var division) is { } findError)
                return EditResult.Fail(findError);
            int attached = FrameLayout.GetMembersEndingOn(frame, division!).Count(p => !ids.Contains(p.Id));
            if (attached > 0)
                return EditResult.Fail($"{Members.Describe(division!)} has {attached} division(s) ending on it. Delete those first.");
        }

        return TryCommit(frame, rules, "Cannot delete the division: ", scratch =>
        {
            scratch.Profiles.RemoveAll(p => ids.Contains(p.Id));
            return null;
        });
    }

    // ── Internals ───────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="edit"/> on a copy (returning an error or null), validates, re-derives glass,
    /// then writes back. The original frame is only touched on success.
    /// </summary>
    private static EditResult TryCommit(Frame frame, DesignRules rules, string errorPrefix, Func<Frame, string?> edit)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rules);

        var scratch = FrameSnapshot.Capture(frame).ToFrame();
        if (edit(scratch) is { } editError)
            return EditResult.Fail(errorPrefix + editError);

        var layout = FrameLayout.Compute(scratch, rules);
        if (!layout.IsValid)
            return EditResult.Fail(errorPrefix + layout.Errors[0]);

        RegenerateGlass(scratch, layout, rules);
        FrameSnapshot.Capture(scratch).ApplyTo(frame);
        return EditResult.Ok;
    }

    /// <summary>
    /// Moves a structural member to a new position along its perpendicular axis, and re-attaches the ends of
    /// perpendicular members that end on it (T-junctions), so connected geometry stays connected.
    /// Returns an error message, or null.
    /// </summary>
    private static string? MoveMember(Frame frame, Profile member, double newPosition)
    {
        if (Members.AxisOf(member) is not { } axis)
            return $"{Members.Describe(member)} is not straight.";
        double oldPosition = Members.PositionOf(member, axis);
        var (start, end) = Members.SpanOf(member, axis);

        foreach (var other in frame.Profiles)
        {
            if (other.Id == member.Id || !Members.IsStructural(other) || Members.AxisOf(other) != Members.Perpendicular(axis))
                continue;
            other.StartPoint = Reattach(other.StartPoint);
            other.EndPoint = Reattach(other.EndPoint);
        }

        member.StartPoint = Place(member.StartPoint);
        member.EndPoint = Place(member.EndPoint);
        return null;

        Point2D Reattach(Point2D p)
        {
            double across = axis == MemberAxis.Vertical ? p.X : p.Y;
            double along = axis == MemberAxis.Vertical ? p.Y : p.X;
            bool endsOnMember = Math.Abs(across - oldPosition) <= Tol && along >= start - Tol && along <= end + Tol;
            return endsOnMember ? Place(p) : p;
        }

        Point2D Place(Point2D p) => axis == MemberAxis.Vertical ? new Point2D(newPosition, p.Y) : new Point2D(p.X, newPosition);
    }

    /// <summary>
    /// Replaces the frame's glass with one panel per layout region. Existing panels keep their Id, thickness and
    /// properties: matched by order when the number of openings is unchanged (moves, resizes), otherwise by the
    /// opening that now contains the old panel's centre (splits, merges).
    /// </summary>
    private static void RegenerateGlass(Frame frame, FrameLayoutResult layout, DesignRules rules)
    {
        var old = frame.GlassPanels
            .OrderBy(g => g.Boundary.Top).ThenBy(g => g.Boundary.Left)
            .ToList();
        var used = new HashSet<Guid>();
        var result = new List<GlassPanel>(layout.Regions.Count);

        for (int i = 0; i < layout.Regions.Count; i++)
        {
            var region = layout.Regions[i];
            GlassPanel? match = old.Count == layout.Regions.Count
                ? old[i]
                : old.FirstOrDefault(g => !used.Contains(g.Id) && region.GlassBounds.Contains(g.Boundary.Center));

            if (match is not null)
            {
                used.Add(match.Id);
                result.Add(new GlassPanel
                {
                    Id = match.Id,
                    Boundary = region.GlassBounds,
                    Thickness = match.Thickness,
                    Properties = new Dictionary<string, string>(match.Properties)
                });
            }
            else
            {
                result.Add(new GlassPanel { Boundary = region.GlassBounds, Thickness = rules.DefaultGlassThicknessMm });
            }
        }

        frame.GlassPanels = result;
    }

    /// <summary>Middle of the widest gap between existing parallel members across <paramref name="area"/>.</summary>
    private static double DefaultPosition(Frame frame, MemberAxis axis, Rectangle2D area, bool wholeOpening)
    {
        double lower = axis == MemberAxis.Vertical ? area.Left : area.Top;
        double upper = axis == MemberAxis.Vertical ? area.Right : area.Bottom;
        if (!wholeOpening)
            return (lower + upper) / 2.0;

        var positions = FrameLayout.BuildMembers(frame)
            .Where(m => m.Axis == axis)
            .Select(m => m.Position)
            .Where(p => p >= lower - Tol && p <= upper + Tol)
            .Append(lower).Append(upper)
            .OrderBy(p => p)
            .ToList();

        double bestLow = lower, bestHigh = upper;
        for (int i = 0; i + 1 < positions.Count; i++)
        {
            if (positions[i + 1] - positions[i] > bestHigh - bestLow || i == 0)
                (bestLow, bestHigh) = (positions[i], positions[i + 1]);
        }
        return (bestLow + bestHigh) / 2.0;
    }

    private static string? FindDivision(Frame frame, Guid id, out Profile? division)
    {
        division = frame.Profiles.FirstOrDefault(p => p.Id == id);
        if (division is null) return "The selected profile is not part of this frame.";
        if (!Members.IsDivision(division)) return "Only mullions and transoms can be moved or deleted individually.";
        return null;
    }

    private static string? CheckFrameSize(double width, double height, double sideThickness, double headThickness, DesignRules rules)
    {
        if (!ValidationHelper.IsValidDimension(width))
            return $"Width must be between {Members.Format(Units.MinDimensionMm)} and {Members.Format(Units.MaxDimensionMm)} mm.";
        if (!ValidationHelper.IsValidDimension(height))
            return $"Height must be between {Members.Format(Units.MinDimensionMm)} and {Members.Format(Units.MaxDimensionMm)} mm.";

        double minWidth = 2 * sideThickness + rules.MinGlassSizeMm;
        double minHeight = 2 * headThickness + rules.MinGlassSizeMm;
        if (width < minWidth || height < minHeight)
            return $"The frame must be at least {Members.Format(minWidth)} × {Members.Format(minHeight)} mm " +
                   $"(two {Members.Format(sideThickness)} mm frame profiles plus {Members.Format(rules.MinGlassSizeMm)} mm of glass).";
        return null;
    }

    private static IEnumerable<Profile> BuildOuterProfiles(double width, double height, double thickness)
    {
        double h = thickness / 2.0;
        Profile Outer(double x1, double y1, double x2, double y2) => new()
        {
            ProfileType = ProfileType.Frame,
            StartPoint = new Point2D(x1, y1),
            EndPoint = new Point2D(x2, y2),
            Thickness = thickness
        };

        yield return Outer(h, h, h, height - h);                  // left
        yield return Outer(h, h, width - h, h);                   // top (head)
        yield return Outer(width - h, h, width - h, height - h);  // right
        yield return Outer(h, height - h, width - h, height - h); // bottom (sill)
    }
}
