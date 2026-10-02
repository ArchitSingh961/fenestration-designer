using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>An enclosed opening of a frame (frame-relative mm).</summary>
/// <param name="CenterlineBounds">The rectangle through the centrelines of the members around it.</param>
/// <param name="GlassBounds">The face-to-face opening, i.e. the geometric glass size (no manufacturing allowances).</param>
public readonly record struct LayoutRegion(Rectangle2D CenterlineBounds, Rectangle2D GlassBounds);

/// <summary>Result of <see cref="FrameLayout.Compute"/>: the openings, or why the structure is invalid.</summary>
public sealed class FrameLayoutResult
{
    internal FrameLayoutResult(IReadOnlyList<LayoutRegion> regions, IReadOnlyList<string> errors)
    {
        Regions = regions;
        Errors = errors;
    }

    /// <summary>Openings ordered top-to-bottom, then left-to-right. Empty when invalid.</summary>
    public IReadOnlyList<LayoutRegion> Regions { get; }

    /// <summary>User-facing descriptions of every rule the structure breaks.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    /// <summary>The region whose glass opening matches <paramref name="glassBounds"/>, if any.</summary>
    public LayoutRegion? FindByGlass(Rectangle2D glassBounds)
    {
        foreach (var region in Regions)
            if (region.GlassBounds.AlmostEquals(glassBounds))
                return region;
        return null;
    }
}

/// <summary>
/// Derives the enclosed openings (future glass panels) of a frame from its structural members, and
/// validates that structure. Pure geometry on domain data: no WPF, no state, so the calculation engine
/// can call it too.
///
/// Algorithm (all members are axis-aligned):
/// <list type="number">
///   <item>Collect the distinct X positions of vertical members and Y positions of horizontal members.
///         They cut the frame's centreline rectangle into a grid of cells.</item>
///   <item>Merge neighbouring cells unless a member covers the edge between them (union-find).</item>
///   <item>Each merged group is one opening. It must be a full rectangle; its glass is the centreline
///         rectangle shrunk to the faces of the surrounding members.</item>
/// </list>
/// This handles any number of full-width or partial divisions, crossings and T-junctions.
/// </summary>
public static class FrameLayout
{
    private const double Tol = GeometryTolerance.Default;

    internal readonly record struct Member(Profile Profile, MemberAxis Axis, double Position, double SpanStart, double SpanEnd)
    {
        public double Half => Profile.Thickness / 2.0;
    }

    public static FrameLayoutResult Compute(Frame frame, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new List<string>();
        var outer = FrameMembers.Find(frame);
        if (outer is null)
            return Fail("The frame must have exactly four outer frame profiles (two vertical, two horizontal).");

        var members = new List<Member>();
        foreach (var profile in frame.Profiles.Where(Members.IsStructural))
        {
            if (!profile.StartPoint.IsFinite || !profile.EndPoint.IsFinite)
            {
                errors.Add($"{Members.Describe(profile)} has invalid coordinates.");
                continue;
            }

            var axis = Members.AxisOf(profile);
            if (axis is null)
            {
                errors.Add($"{Members.Describe(profile)} must be vertical or horizontal.");
                continue;
            }
            if (profile.ProfileType == ProfileType.Mullion && axis != MemberAxis.Vertical)
                errors.Add("A mullion must be vertical.");
            if (profile.ProfileType == ProfileType.Transom && axis != MemberAxis.Horizontal)
                errors.Add("A transom must be horizontal.");

            var (start, end) = Members.SpanOf(profile, axis.Value);
            members.Add(new Member(profile, axis.Value, Members.PositionOf(profile, axis.Value), start, end));
        }
        if (errors.Count > 0) return Fail(errors);

        var loop = outer.CenterlineLoop;
        if (loop.Width <= Tol || loop.Height <= Tol)
            return Fail("The frame is too small for its frame profiles.");

        CheckInsideFrame(members, loop, errors);
        CheckCollinearOverlaps(members, errors);
        if (errors.Count > 0) return Fail(errors);

        CheckEndsConnected(members, errors);
        if (errors.Count > 0) return Fail(errors);

        var regions = BuildRegions(members, rules, errors);
        return errors.Count > 0 ? Fail(errors) : new FrameLayoutResult(regions, Array.Empty<string>());
    }

    /// <summary>
    /// The visible body of a profile (frame-relative). A division is drawn and hit-tested face to face:
    /// each end that meets another member stops at that member's face instead of its centreline.
    /// Outer frame members span their full length (their corners overlap, as with mitred corners).
    /// </summary>
    public static Rectangle2D GetMemberBody(Frame frame, Profile profile)
    {
        var axis = Members.AxisOf(profile);
        if (axis is null || !Members.IsDivision(profile))
            return profile.GetBounds();

        var others = BuildMembers(frame).Where(m => m.Profile.Id != profile.Id).ToList();
        double position = Members.PositionOf(profile, axis.Value);
        var (start, end) = Members.SpanOf(profile, axis.Value);
        double half = profile.Thickness / 2.0;

        double trimmedStart = start + HalfOfMemberAt(others, Members.Perpendicular(axis.Value), start, position);
        double trimmedEnd = end - HalfOfMemberAt(others, Members.Perpendicular(axis.Value), end, position);
        if (trimmedEnd < trimmedStart)
            (trimmedStart, trimmedEnd) = (start, end);

        return axis == MemberAxis.Vertical
            ? Rectangle2D.FromCorners(new Point2D(position - half, trimmedStart), new Point2D(position + half, trimmedEnd))
            : Rectangle2D.FromCorners(new Point2D(trimmedStart, position - half), new Point2D(trimmedEnd, position + half));
    }

    /// <summary>
    /// Endpoints of perpendicular structural members that lie on <paramref name="profile"/>'s centreline,
    /// i.e. members that end on it (T-junctions). Moving the profile drags these endpoints with it.
    /// </summary>
    public static int CountMembersEndingOn(Frame frame, Profile profile) => GetMembersEndingOn(frame, profile).Count;

    /// <summary>The perpendicular structural members with an end on <paramref name="profile"/>'s centreline.</summary>
    public static IReadOnlyList<Profile> GetMembersEndingOn(Frame frame, Profile profile)
    {
        var axis = Members.AxisOf(profile);
        if (axis is null) return Array.Empty<Profile>();
        double position = Members.PositionOf(profile, axis.Value);
        var (start, end) = Members.SpanOf(profile, axis.Value);

        var result = new List<Profile>();
        foreach (var other in BuildMembers(frame))
        {
            if (other.Axis == axis || other.Profile.Id == profile.Id) continue;
            if (EndsOn(other.Profile.StartPoint) || EndsOn(other.Profile.EndPoint)) result.Add(other.Profile);
        }
        return result;

        bool EndsOn(Point2D p)
        {
            double across = axis == MemberAxis.Vertical ? p.X : p.Y;
            double along = axis == MemberAxis.Vertical ? p.Y : p.X;
            return Math.Abs(across - position) <= Tol && along >= start - Tol && along <= end + Tol;
        }
    }

    // ── Validation steps ────────────────────────────────────────────

    private static void CheckInsideFrame(List<Member> members, Rectangle2D loop, List<string> errors)
    {
        foreach (var m in members.Where(m => Members.IsDivision(m.Profile)))
        {
            if (!loop.Contains(m.Profile.StartPoint, Tol) || !loop.Contains(m.Profile.EndPoint, Tol))
                errors.Add($"{Members.Describe(m.Profile)} lies outside the frame.");
        }
    }

    private static void CheckCollinearOverlaps(List<Member> members, List<string> errors)
    {
        for (int i = 0; i < members.Count; i++)
        for (int j = i + 1; j < members.Count; j++)
        {
            var a = members[i];
            var b = members[j];
            if (a.Axis != b.Axis || Math.Abs(a.Position - b.Position) > Tol) continue;
            double overlap = Math.Min(a.SpanEnd, b.SpanEnd) - Math.Max(a.SpanStart, b.SpanStart);
            if (overlap > Tol)
            {
                string axisName = a.Axis == MemberAxis.Vertical ? "X" : "Y";
                errors.Add($"Two profiles are at the same position ({axisName} = {Members.Format(a.Position)} mm).");
            }
        }
    }

    private static void CheckEndsConnected(List<Member> members, List<string> errors)
    {
        foreach (var m in members)
        {
            foreach (var end in new[] { m.Profile.StartPoint, m.Profile.EndPoint })
            {
                if (!IsConnected(m, end, members))
                    errors.Add($"{Members.Describe(m.Profile)} ends at ({Members.Format(end.X)}, {Members.Format(end.Y)}) " +
                               "without meeting the frame or another division.");
            }
        }
    }

    private static bool IsConnected(Member member, Point2D end, List<Member> members)
    {
        foreach (var other in members)
        {
            if (other.Axis == member.Axis) continue;
            double across = other.Axis == MemberAxis.Vertical ? end.X : end.Y;
            double along = other.Axis == MemberAxis.Vertical ? end.Y : end.X;
            if (Math.Abs(across - other.Position) <= Tol && along >= other.SpanStart - Tol && along <= other.SpanEnd + Tol)
                return true;
        }
        return false;
    }

    // ── Region extraction ───────────────────────────────────────────

    private static List<LayoutRegion> BuildRegions(List<Member> members, DesignRules rules, List<string> errors)
    {
        var xs = DistinctPositions(members, MemberAxis.Vertical);
        var ys = DistinctPositions(members, MemberAxis.Horizontal);
        int nx = xs.Count - 1, ny = ys.Count - 1;
        var sets = new UnionFind(nx * ny);
        int Cell(int i, int j) => j * nx + i;

        for (int j = 0; j < ny; j++)
        for (int i = 0; i + 1 < nx; i++)
            if (!IsCovered(members, MemberAxis.Vertical, xs[i + 1], ys[j], ys[j + 1]))
                sets.Union(Cell(i, j), Cell(i + 1, j));

        for (int i = 0; i < nx; i++)
        for (int j = 0; j + 1 < ny; j++)
            if (!IsCovered(members, MemberAxis.Horizontal, ys[j + 1], xs[i], xs[i + 1]))
                sets.Union(Cell(i, j), Cell(i, j + 1));

        var groups = new Dictionary<int, (int IMin, int IMax, int JMin, int JMax, int Count)>();
        for (int j = 0; j < ny; j++)
        for (int i = 0; i < nx; i++)
        {
            int root = sets.Find(Cell(i, j));
            groups[root] = groups.TryGetValue(root, out var g)
                ? (Math.Min(g.IMin, i), Math.Max(g.IMax, i), Math.Min(g.JMin, j), Math.Max(g.JMax, j), g.Count + 1)
                : (i, i, j, j, 1);
        }

        var regions = new List<LayoutRegion>();
        foreach (var g in groups.Values)
        {
            if (g.Count != (g.IMax - g.IMin + 1) * (g.JMax - g.JMin + 1))
            {
                errors.Add("An opening is not rectangular; every division must run between two other profiles.");
                continue;
            }

            var centerline = Rectangle2D.FromCorners(new Point2D(xs[g.IMin], ys[g.JMin]), new Point2D(xs[g.IMax + 1], ys[g.JMax + 1]));
            double left = centerline.Left + HalfAt(members, MemberAxis.Vertical, centerline.Left, centerline.Top, centerline.Bottom);
            double right = centerline.Right - HalfAt(members, MemberAxis.Vertical, centerline.Right, centerline.Top, centerline.Bottom);
            double top = centerline.Top + HalfAt(members, MemberAxis.Horizontal, centerline.Top, centerline.Left, centerline.Right);
            double bottom = centerline.Bottom - HalfAt(members, MemberAxis.Horizontal, centerline.Bottom, centerline.Left, centerline.Right);

            double width = right - left, height = bottom - top;
            if (width < rules.MinGlassSizeMm - Tol || height < rules.MinGlassSizeMm - Tol)
            {
                errors.Add($"The opening between X {Members.Format(centerline.Left)}–{Members.Format(centerline.Right)} mm and " +
                           $"Y {Members.Format(centerline.Top)}–{Members.Format(centerline.Bottom)} mm would leave only " +
                           $"{Members.Format(Math.Max(width, 0))} × {Members.Format(Math.Max(height, 0))} mm of glass " +
                           $"(minimum {Members.Format(rules.MinGlassSizeMm)} mm).");
                continue;
            }

            regions.Add(new LayoutRegion(centerline, Rectangle2D.FromCorners(new Point2D(left, top), new Point2D(right, bottom))));
        }

        regions.Sort((a, b) => a.CenterlineBounds.Top != b.CenterlineBounds.Top
            ? a.CenterlineBounds.Top.CompareTo(b.CenterlineBounds.Top)
            : a.CenterlineBounds.Left.CompareTo(b.CenterlineBounds.Left));
        return regions;
    }

    /// <summary>True if a member on <paramref name="axis"/> at <paramref name="position"/> covers [from, to] completely.</summary>
    private static bool IsCovered(List<Member> members, MemberAxis axis, double position, double from, double to)
        => members.Any(m => m.Axis == axis && Math.Abs(m.Position - position) <= Tol
                            && m.SpanStart <= from + Tol && m.SpanEnd >= to - Tol);

    /// <summary>Largest half-thickness of members on <paramref name="axis"/> at <paramref name="position"/> overlapping (from, to).</summary>
    private static double HalfAt(List<Member> members, MemberAxis axis, double position, double from, double to)
    {
        double half = 0;
        foreach (var m in members)
            if (m.Axis == axis && Math.Abs(m.Position - position) <= Tol && m.SpanStart < to - Tol && m.SpanEnd > from + Tol)
                half = Math.Max(half, m.Half);
        return half;
    }

    /// <summary>Half-thickness of the member on <paramref name="axis"/> at <paramref name="position"/> that passes through <paramref name="crossing"/>.</summary>
    private static double HalfOfMemberAt(List<Member> members, MemberAxis axis, double position, double crossing)
    {
        double half = 0;
        foreach (var m in members)
            if (m.Axis == axis && Math.Abs(m.Position - position) <= Tol
                && crossing >= m.SpanStart - Tol && crossing <= m.SpanEnd + Tol)
                half = Math.Max(half, m.Half);
        return half;
    }

    private static List<double> DistinctPositions(List<Member> members, MemberAxis axis)
    {
        var result = new List<double>();
        foreach (double p in members.Where(m => m.Axis == axis).Select(m => m.Position).OrderBy(p => p))
            if (result.Count == 0 || p - result[^1] > Tol)
                result.Add(p);
        return result;
    }

    internal static List<Member> BuildMembers(Frame frame)
    {
        var members = new List<Member>();
        foreach (var profile in frame.Profiles.Where(Members.IsStructural))
        {
            if (Members.AxisOf(profile) is not { } axis) continue;
            var (start, end) = Members.SpanOf(profile, axis);
            members.Add(new Member(profile, axis, Members.PositionOf(profile, axis), start, end));
        }
        return members;
    }

    private static FrameLayoutResult Fail(string error) => new(Array.Empty<LayoutRegion>(), new[] { error });

    private static FrameLayoutResult Fail(List<string> errors) => new(Array.Empty<LayoutRegion>(), errors.Distinct().ToList());

    private sealed class UnionFind
    {
        private readonly int[] _parent;

        public UnionFind(int count)
        {
            _parent = new int[count];
            for (int i = 0; i < count; i++) _parent[i] = i;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
                x = _parent[x] = _parent[_parent[x]];
            return x;
        }

        public void Union(int a, int b) => _parent[Find(a)] = Find(b);
    }
}
