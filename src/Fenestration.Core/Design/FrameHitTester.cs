using Fenestration.Core.Geometry;
using Fenestration.Core.Models;

namespace Fenestration.Core.Design;

/// <summary>The kind of design element under a point.</summary>
public enum DesignElementKind
{
    Frame,
    Mullion,
    Transom,
    Glass,
    /// <summary>A Sash or Generic profile.</summary>
    Profile
}

/// <summary>A hit-test result: what was hit and which frame it belongs to.</summary>
public readonly record struct DesignHit(DesignElementKind Kind, Guid ElementId, Guid FrameId);

/// <summary>
/// Finds the design element at a WORLD point (mm). The caller converts the mouse position with
/// <c>ScreenToWorld</c> and passes a tolerance converted from pixels to mm, so picking behaves the same at
/// every zoom level.
///
/// Priority: divisions (nearest centreline wins) → glass → frame. Clicking the outer frame profiles
/// selects the frame itself, which is what the user edits (width/height).
/// </summary>
public static class FrameHitTester
{
    public static DesignHit? HitTest(Project project, Point2D worldPoint, double toleranceMm)
    {
        ArgumentNullException.ThrowIfNull(project);
        GeometryValidation.EnsureValidTolerance(toleranceMm);
        if (!worldPoint.IsFinite) return null;

        // Last frame is drawn on top, so test it first.
        for (int i = project.Frames.Count - 1; i >= 0; i--)
        {
            var hit = HitTest(project.Frames[i], worldPoint, toleranceMm);
            if (hit is not null) return hit;
        }
        return null;
    }

    public static DesignHit? HitTest(Frame frame, Point2D worldPoint, double toleranceMm)
    {
        if (!frame.Bounds.Contains(worldPoint, toleranceMm))
            return null;

        var local = new Point2D(worldPoint.X - frame.X, worldPoint.Y - frame.Y);

        Profile? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (var profile in frame.Profiles.Where(p => p.ProfileType != ProfileType.Frame))
        {
            if (!FrameLayout.GetMemberBody(frame, profile).Contains(local, toleranceMm)) continue;
            double distance = profile.Segment.DistanceTo(local);
            if (distance < nearestDistance)
                (nearest, nearestDistance) = (profile, distance);
        }

        if (nearest is not null)
        {
            var kind = nearest.ProfileType switch
            {
                ProfileType.Mullion => DesignElementKind.Mullion,
                ProfileType.Transom => DesignElementKind.Transom,
                _ => DesignElementKind.Profile
            };
            return new DesignHit(kind, nearest.Id, frame.Id);
        }

        var glass = frame.GlassPanels.FirstOrDefault(g => g.Boundary.Contains(local));
        if (glass is not null)
            return new DesignHit(DesignElementKind.Glass, glass.Id, frame.Id);

        return new DesignHit(DesignElementKind.Frame, frame.Id, frame.Id);
    }
}
