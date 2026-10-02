using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>The edge of a sash its handle is on.</summary>
public enum HandleSide { Left, Right, Top, Bottom }

/// <summary>
/// The derived drawing geometry of one sash (frame-relative mm).
/// </summary>
/// <param name="Outer">The sash's outer edge: the opening, extended to the meeting line between interlocking sliding panels.</param>
/// <param name="Glass">The glass visible inside the sash band.</param>
/// <param name="Handle">Centre of the handle, on the sash band.</param>
/// <param name="Side">The sash edge the handle is on.</param>
/// <param name="HandleHeightMm">Height of the handle above the bottom of the frame (side handles only), else null.</param>
public readonly record struct SashLayout(Rectangle2D Outer, Rectangle2D Glass, Point2D Handle, HandleSide Side, double? HandleHeightMm);

/// <summary>
/// Sash geometry derived from an opening and its type. Pure functions on domain data (no WPF), shared by the
/// renderer and, later, by the calculation of sash members.
/// </summary>
public static class OpeningGeometry
{
    private const double Tol = GeometryTolerance.Default;

    /// <summary>The sash of <paramref name="panel"/>, or null for a fixed opening.</summary>
    public static SashLayout? SashOf(Frame frame, GlassPanel panel, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(rules);
        if (!panel.Opening.IsOpenable()) return null;

        var outer = panel.Opening.IsSliding() ? ExtendToMeetingLines(frame, panel) : panel.Boundary;
        double band = Math.Min(rules.SashFaceWidthMm, Math.Min(outer.Width, outer.Height) / 4.0);
        var glass = outer.Inflate(-band);

        var side = HandleSideOf(panel.Opening);
        var handle = side switch
        {
            HandleSide.Left => new Point2D(outer.Left + band / 2, outer.Center.Y),
            HandleSide.Right => new Point2D(outer.Right - band / 2, outer.Center.Y),
            HandleSide.Top => new Point2D(outer.Center.X, outer.Top + band / 2),
            _ => new Point2D(outer.Center.X, outer.Bottom - band / 2)
        };
        double? height = side is HandleSide.Left or HandleSide.Right ? frame.Height - handle.Y : null;
        return new SashLayout(outer, glass, handle, side, height);
    }

    /// <summary>
    /// Where the handle goes: opposite the hinges for hinged sashes; on the edge you pull from (opposite the
    /// direction of travel) for sliding ones.
    /// </summary>
    public static HandleSide HandleSideOf(OpeningType type) => type switch
    {
        OpeningType.SideHungRight or OpeningType.TiltTurnRight => HandleSide.Left,
        OpeningType.TopHung => HandleSide.Bottom,
        OpeningType.BottomHung => HandleSide.Top,
        OpeningType.PivotHorizontal => HandleSide.Bottom,
        OpeningType.SlidingRight => HandleSide.Left,
        OpeningType.SlidingLeft => HandleSide.Right,
        OpeningType.SlidingUp => HandleSide.Bottom,
        OpeningType.SlidingDown => HandleSide.Top,
        _ => HandleSide.Right
    };

    /// <summary>
    /// Sliding panels side by side interlock: where a sliding panel meets another one sliding the same way
    /// (horizontally or vertically) across a division, both sashes run to the division's centreline, covering it.
    /// </summary>
    private static Rectangle2D ExtendToMeetingLines(Frame frame, GlassPanel panel)
    {
        bool horizontal = panel.Opening is OpeningType.SlidingLeft or OpeningType.SlidingRight;
        var box = panel.Boundary;
        double left = box.Left, right = box.Right, top = box.Top, bottom = box.Bottom;
        double maxGap = frame.Profiles.Where(Members.IsDivision).Select(p => p.Thickness).DefaultIfEmpty(0).Max() + Tol;

        foreach (var other in frame.GlassPanels)
        {
            if (other.Id == panel.Id || !other.Opening.IsSliding()) continue;
            bool otherHorizontal = other.Opening is OpeningType.SlidingLeft or OpeningType.SlidingRight;
            if (otherHorizontal != horizontal) continue;
            var o = other.Boundary;

            if (horizontal && Overlaps(box.Top, box.Bottom, o.Top, o.Bottom))
            {
                if (o.Left >= box.Right - Tol && o.Left - box.Right <= maxGap) right = (box.Right + o.Left) / 2;
                if (o.Right <= box.Left + Tol && box.Left - o.Right <= maxGap) left = (o.Right + box.Left) / 2;
            }
            else if (!horizontal && Overlaps(box.Left, box.Right, o.Left, o.Right))
            {
                if (o.Top >= box.Bottom - Tol && o.Top - box.Bottom <= maxGap) bottom = (box.Bottom + o.Top) / 2;
                if (o.Bottom <= box.Top + Tol && box.Top - o.Bottom <= maxGap) top = (o.Bottom + box.Top) / 2;
            }
        }
        return Rectangle2D.FromCorners(new Point2D(left, top), new Point2D(right, bottom));
    }

    private static bool Overlaps(double a1, double a2, double b1, double b2) => Math.Min(a2, b2) - Math.Max(a1, b1) > Tol;

    /// <summary>
    /// A copy of <paramref name="frame"/> as seen from the other side of the wall: mirrored left-to-right about its
    /// own centre (it stays in place on the drawing), with hinge sides and sliding directions swapped. Ids are kept,
    /// so selection still highlights the same objects. Used for the Outside view; the model is never changed.
    /// </summary>
    public static Frame Mirror(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var copy = FrameSnapshot.Capture(frame).ToFrame();
        double w = copy.Width;
        Point2D Flip(Point2D p) => new(w - p.X, p.Y);
        Rectangle2D FlipRect(Rectangle2D r) => Rectangle2D.FromCorners(Flip(r.TopLeft), Flip(r.BottomRight));

        foreach (var profile in copy.Profiles)
        {
            profile.StartPoint = Flip(profile.StartPoint);
            profile.EndPoint = Flip(profile.EndPoint);
        }
        foreach (var panel in copy.GlassPanels)
        {
            panel.Boundary = FlipRect(panel.Boundary);
            panel.Opening = panel.Opening.Mirrored();
        }
        foreach (var dimension in copy.Dimensions)
        {
            dimension.StartPoint = Flip(dimension.StartPoint);
            dimension.EndPoint = Flip(dimension.EndPoint);
        }
        // The panel order is kept, so each opening keeps its number (S1 is the same sash from both sides).
        return copy;
    }
}
