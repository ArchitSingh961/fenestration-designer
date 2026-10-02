using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>Direction a straight structural member runs in.</summary>
public enum MemberAxis
{
    /// <summary>Runs top to bottom (constant X): outer frame sides and mullions.</summary>
    Vertical,

    /// <summary>Runs left to right (constant Y): outer frame head/sill and transoms.</summary>
    Horizontal
}

/// <summary>
/// Helpers for reading profiles as structural members.
///
/// Division model: a mullion or transom is an ordinary <see cref="Profile"/> whose centreline runs
/// between the centrelines of the two members it spans between (outer frame members or other
/// divisions). Its <em>position</em> is the constant coordinate of that centreline, in mm from the
/// frame's outer top-left corner: X for a mullion, Y for a transom.
/// </summary>
public static class Members
{
    /// <summary>Outer frame, mullions and transoms take part in the layout. Sash/Generic profiles do not (yet).</summary>
    public static bool IsStructural(Profile profile)
        => profile.ProfileType is ProfileType.Frame or ProfileType.Mullion or ProfileType.Transom;

    public static bool IsDivision(Profile profile)
        => profile.ProfileType is ProfileType.Mullion or ProfileType.Transom;

    /// <summary>The axis the profile runs along, or null if it is not axis-aligned (or has no length).</summary>
    public static MemberAxis? AxisOf(Profile profile)
    {
        double dx = Math.Abs(profile.EndPoint.X - profile.StartPoint.X);
        double dy = Math.Abs(profile.EndPoint.Y - profile.StartPoint.Y);
        const double tol = GeometryTolerance.Default;
        if (dx <= tol && dy > tol) return MemberAxis.Vertical;
        if (dy <= tol && dx > tol) return MemberAxis.Horizontal;
        return null;
    }

    public static MemberAxis Perpendicular(MemberAxis axis)
        => axis == MemberAxis.Vertical ? MemberAxis.Horizontal : MemberAxis.Vertical;

    /// <summary>The member's position: centreline X for vertical members, centreline Y for horizontal ones.</summary>
    public static double PositionOf(Profile profile, MemberAxis axis)
        => axis == MemberAxis.Vertical ? profile.StartPoint.X : profile.StartPoint.Y;

    /// <summary>The member's extent along its own axis, as (min, max).</summary>
    public static (double Start, double End) SpanOf(Profile profile, MemberAxis axis)
    {
        double a = axis == MemberAxis.Vertical ? profile.StartPoint.Y : profile.StartPoint.X;
        double b = axis == MemberAxis.Vertical ? profile.EndPoint.Y : profile.EndPoint.X;
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>The division's position for display/editing (X for a mullion, Y for a transom).</summary>
    public static double DivisionPosition(Profile division)
        => division.ProfileType == ProfileType.Mullion ? division.StartPoint.X : division.StartPoint.Y;

    /// <summary>Human-readable description used in validation messages, e.g. "The mullion at 600 mm".</summary>
    public static string Describe(Profile profile)
    {
        var axis = AxisOf(profile);
        string where = axis is { } a ? $" at {Format(PositionOf(profile, a))} mm" : "";
        return profile.ProfileType switch
        {
            ProfileType.Mullion => $"The mullion{where}",
            ProfileType.Transom => $"The transom{where}",
            ProfileType.Frame => "The outer frame",
            _ => $"The {profile.ProfileType.ToString().ToLowerInvariant()} profile{where}"
        };
    }

    internal static string Format(double mm) => mm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The four outer frame members of a frame.</summary>
public sealed record FrameMembers(Profile Left, Profile Top, Profile Right, Profile Bottom)
{
    /// <summary>
    /// Finds the outer members (two vertical and two horizontal <see cref="ProfileType.Frame"/> profiles),
    /// or null if the frame doesn't have exactly that set.
    /// </summary>
    public static FrameMembers? Find(Frame frame)
    {
        var outer = frame.Profiles.Where(p => p.ProfileType == ProfileType.Frame).ToList();
        if (outer.Count != 4) return null;

        var vertical = outer.Where(p => Members.AxisOf(p) == MemberAxis.Vertical).OrderBy(p => p.StartPoint.X).ToList();
        var horizontal = outer.Where(p => Members.AxisOf(p) == MemberAxis.Horizontal).OrderBy(p => p.StartPoint.Y).ToList();
        if (vertical.Count != 2 || horizontal.Count != 2) return null;

        return new FrameMembers(vertical[0], horizontal[0], vertical[1], horizontal[1]);
    }

    /// <summary>The rectangle through the four frame centrelines (frame-relative).</summary>
    public Rectangle2D CenterlineLoop => Rectangle2D.FromCorners(
        new Point2D(Left.StartPoint.X, Top.StartPoint.Y),
        new Point2D(Right.StartPoint.X, Bottom.StartPoint.Y));

    /// <summary>The frame's inner (daylight) opening before any divisions (frame-relative).</summary>
    public Rectangle2D InnerOpening => Rectangle2D.FromCorners(
        new Point2D(Left.StartPoint.X + Left.Thickness / 2, Top.StartPoint.Y + Top.Thickness / 2),
        new Point2D(Right.StartPoint.X - Right.Thickness / 2, Bottom.StartPoint.Y - Bottom.Thickness / 2));
}
