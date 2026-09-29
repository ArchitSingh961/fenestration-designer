using Fenestration.Core.Geometry;
using Fenestration.Core.Models;
using Fenestration.Core.Utilities;

namespace Fenestration.Core.Design;

/// <summary>
/// The frame-designer operations: create, resize, add/move/delete divisions. Every operation is ATOMIC:
/// it runs on a copy of the frame, validates the result with <see cref="FrameLayout"/>, re-derives the glass,
/// and only then writes the result back. On failure a <see cref="DesignValidationException"/> is thrown
/// and the frame is left untouched.
/// All positions are frame-relative millimetres (0 = the frame's outer left/top edge).
/// </summary>
public static class FrameEditor
{
    private const double Tol = GeometryTolerance.Default;

    // ── Frame ───────────────────────────────────────────────────────

    /// <summary>Creates a rectangular frame with four outer profiles and one derived glass panel.</summary>
    public static Frame CreateFrame(double x, double y, double width, double height, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new DesignValidationException("The frame position must be a finite number.");
        EnsureFrameSize(width, height, rules.FrameThicknessMm, rules.FrameThicknessMm, rules);

        var frame = new Frame { X = x, Y = y, Width = width, Height = height };
        frame.Profiles.AddRange(BuildOuterProfiles(width, height, rules.FrameThicknessMm));

        var layout = FrameLayout.Compute(frame, rules);
        if (!layout.IsValid)
            throw new DesignValidationException(layout.Errors[0]);
        RegenerateGlass(frame, layout, rules);
        return frame;
    }

    /// <summary>
    /// Changes the outer size. The left and top edges stay put; the right and bottom frame members move,
    /// dragging with them every division that ends on them. Divisions keep their positions, so shrinking
    /// the frame past a division is rejected.
    /// </summary>
    public static void Resize(Frame frame, double width, double height, DesignRules rules)
    {
        var outer = FrameMembers.Find(frame)
            ?? throw new DesignValidationException("The frame has no outer profiles to resize.");
        EnsureFrameSize(width, height, Math.Max(outer.Left.Thickness, outer.Right.Thickness),
            Math.Max(outer.Top.Thickness, outer.Bottom.Thickness), rules);

        Commit(frame, rules, "Cannot resize the frame: ", scratch =>
        {
            var members = FrameMembers.Find(scratch)!;
            MoveMember(scratch, members.Right, width - members.Right.Thickness / 2.0);
            MoveMember(scratch, members.Bottom, height - members.Bottom.Thickness / 2.0);
            scratch.Width = width;
            scratch.Height = height;
        });
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
        var outer = FrameMembers.Find(frame)
            ?? throw new DesignValidationException("The frame has no outer profiles.");
        var current = FrameLayout.Compute(frame, rules);
        if (!current.IsValid)
            throw new DesignValidationException(current.Errors[0]);

        Rectangle2D area = outer.CenterlineLoop;
        if (splitGlassId is { } glassId)
        {
            var glass = frame.GlassPanels.FirstOrDefault(g => g.Id == glassId)
                ?? throw new DesignValidationException("The selected glass panel is not part of this frame.");
            area = (current.FindByGlass(glass.Boundary)
                ?? throw new DesignValidationException("The selected glass panel no longer matches the frame layout.")).CenterlineBounds;
        }

        double lower = axis == MemberAxis.Vertical ? area.Left : area.Top;
        double upper = axis == MemberAxis.Vertical ? area.Right : area.Bottom;
        double pos = position ?? DefaultPosition(frame, axis, area, wholeOpening: splitGlassId is null);
        if (!double.IsFinite(pos) || pos <= lower + Tol || pos >= upper - Tol)
            throw new DesignValidationException(
                $"The position must be between {Members.Format(lower)} and {Members.Format(upper)} mm.");

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

        Commit(frame, rules, "Cannot add the division: ", scratch => scratch.Profiles.Add(profile));
        return profile.Id;
    }

    /// <summary>Moves a mullion (new X) or transom (new Y). Divisions ending on it move their ends with it.</summary>
    public static void MoveDivision(Frame frame, Guid divisionId, double position, DesignRules rules)
    {
        var division = FindDivision(frame, divisionId);
        if (!double.IsFinite(position))
            throw new DesignValidationException("The position must be a number.");
        if (Math.Abs(Members.DivisionPosition(division) - position) <= GeometryTolerance.Epsilon)
            return;

        Commit(frame, rules, $"Cannot move {Members.Describe(division).ToLowerInvariant()}: ",
            scratch => MoveMember(scratch, scratch.Profiles.First(p => p.Id == divisionId), position));
    }

    /// <summary>True if <see cref="MoveDivision"/> would succeed. Nothing is modified.</summary>
    public static bool CanMoveDivision(Frame frame, Guid divisionId, double position, DesignRules rules)
    {
        if (!double.IsFinite(position)) return false;
        var scratch = FrameSnapshot.Capture(frame).ToFrame();
        var division = scratch.Profiles.FirstOrDefault(p => p.Id == divisionId);
        if (division is null || !Members.IsDivision(division)) return false;
        MoveMember(scratch, division, position);
        return FrameLayout.Compute(scratch, rules).IsValid;
    }

    /// <summary>Removes a mullion or transom. Rejected while other divisions end on it (they would be left hanging).</summary>
    public static void DeleteDivision(Frame frame, Guid divisionId, DesignRules rules)
    {
        var division = FindDivision(frame, divisionId);
        int attached = FrameLayout.CountMembersEndingOn(frame, division);
        if (attached > 0)
            throw new DesignValidationException(
                $"{Members.Describe(division)} has {attached} division(s) ending on it. Delete those first.");

        Commit(frame, rules, "Cannot delete the division: ", scratch => scratch.Profiles.RemoveAll(p => p.Id == divisionId));
    }

    // ── Internals ───────────────────────────────────────────────────

    /// <summary>Runs <paramref name="edit"/> on a copy, validates, re-derives glass, then writes back.</summary>
    private static void Commit(Frame frame, DesignRules rules, string errorPrefix, Action<Frame> edit)
    {
        var scratch = FrameSnapshot.Capture(frame).ToFrame();
        edit(scratch);

        var layout = FrameLayout.Compute(scratch, rules);
        if (!layout.IsValid)
            throw new DesignValidationException(errorPrefix + layout.Errors[0]);

        RegenerateGlass(scratch, layout, rules);
        FrameSnapshot.Capture(scratch).ApplyTo(frame);
    }

    /// <summary>
    /// Moves a structural member to a new position along its perpendicular axis, and re-attaches the ends of
    /// perpendicular members that end on it (T-junctions), so connected geometry stays connected.
    /// </summary>
    private static void MoveMember(Frame frame, Profile member, double newPosition)
    {
        var axis = Members.AxisOf(member) ?? throw new DesignValidationException($"{Members.Describe(member)} is not straight.");
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

    private static Profile FindDivision(Frame frame, Guid id)
    {
        var profile = frame.Profiles.FirstOrDefault(p => p.Id == id)
            ?? throw new DesignValidationException("The selected profile is not part of this frame.");
        if (!Members.IsDivision(profile))
            throw new DesignValidationException("Only mullions and transoms can be moved or deleted individually.");
        return profile;
    }

    private static void EnsureFrameSize(double width, double height, double sideThickness, double headThickness, DesignRules rules)
    {
        if (!ValidationHelper.IsValidDimension(width))
            throw new DesignValidationException(
                $"Width must be between {Members.Format(Units.MinDimensionMm)} and {Members.Format(Units.MaxDimensionMm)} mm.");
        if (!ValidationHelper.IsValidDimension(height))
            throw new DesignValidationException(
                $"Height must be between {Members.Format(Units.MinDimensionMm)} and {Members.Format(Units.MaxDimensionMm)} mm.");

        double minWidth = 2 * sideThickness + rules.MinGlassSizeMm;
        double minHeight = 2 * headThickness + rules.MinGlassSizeMm;
        if (width < minWidth || height < minHeight)
            throw new DesignValidationException(
                $"The frame must be at least {Members.Format(minWidth)} × {Members.Format(minHeight)} mm " +
                $"(two {Members.Format(sideThickness)} mm frame profiles plus {Members.Format(rules.MinGlassSizeMm)} mm of glass).");
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
