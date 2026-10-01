using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Core.Design;

/// <summary>
/// The controlled resize handles of a frame. Only the right edge (width), bottom edge (height) and
/// bottom-right corner (both) are offered: the left and top edges anchor the frame, because every
/// division position is measured from them. There is no free-form scaling.
/// </summary>
public enum FrameHandle
{
    Right,
    Bottom,
    BottomRight
}

public static class FrameHandles
{
    public static bool ChangesWidth(FrameHandle handle) => handle is FrameHandle.Right or FrameHandle.BottomRight;

    public static bool ChangesHeight(FrameHandle handle) => handle is FrameHandle.Bottom or FrameHandle.BottomRight;

    /// <summary>World position of each handle.</summary>
    public static IReadOnlyList<(FrameHandle Handle, Point2D Position)> GetHandles(Frame frame) => new[]
    {
        (FrameHandle.Right, new Point2D(frame.X + frame.Width, frame.Y + frame.Height / 2.0)),
        (FrameHandle.Bottom, new Point2D(frame.X + frame.Width / 2.0, frame.Y + frame.Height)),
        (FrameHandle.BottomRight, new Point2D(frame.X + frame.Width, frame.Y + frame.Height))
    };

    /// <summary>
    /// The handle within <paramref name="toleranceMm"/> of <paramref name="worldPoint"/> (nearest wins), or null.
    /// The caller converts its on-screen handle size to mm, so handles are equally easy to grab at every zoom.
    /// </summary>
    public static FrameHandle? HitTest(Frame frame, Point2D worldPoint, double toleranceMm)
    {
        GeometryValidation.EnsureValidTolerance(toleranceMm);
        FrameHandle? best = null;
        double bestDistance = double.MaxValue;
        foreach (var (handle, position) in GetHandles(frame))
        {
            // Square handles: use the larger axis distance (Chebyshev) so the whole square is grabbable.
            double distance = Math.Max(Math.Abs(position.X - worldPoint.X), Math.Abs(position.Y - worldPoint.Y));
            if (distance <= toleranceMm && distance < bestDistance)
                (best, bestDistance) = (handle, distance);
        }
        return best;
    }
}
