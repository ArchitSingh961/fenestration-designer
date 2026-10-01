using Fenestration.Core.Design;
using Fenestration.Core.Geometry;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;

namespace Fenestration.Core.Snapping;

/// <summary>
/// A modular source of snap targets of one <see cref="SnapType"/>. Providers are asked ONCE when an
/// interaction starts (the committed model doesn't change during a drag), so each mouse move only compares
/// distances against a prepared list.
/// </summary>
public interface ISnapTargetProvider
{
    SnapType Type { get; }

    void CollectTargets(SnapScene scene, List<SnapTarget> targets);
}

/// <summary>Frame corners and the endpoints of structural centrelines.</summary>
public sealed class EndpointSnapProvider : ISnapTargetProvider
{
    public SnapType Type => SnapType.Endpoint;

    public void CollectTargets(SnapScene scene, List<SnapTarget> targets)
    {
        foreach (var frame in scene.Frames)
        {
            var b = frame.Bounds;
            foreach (var corner in new[] { b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft })
                targets.Add(SnapTarget.AtPoint(Type, corner));

            var offset = SnapScene.OffsetOf(frame);
            foreach (var profile in scene.StructuralProfiles(frame))
                foreach (var end in new[] { profile.StartPoint + offset, profile.EndPoint + offset })
                    if (!scene.LiesOnExcludedGeometry(end))
                        targets.Add(SnapTarget.AtPoint(Type, end));
        }
    }
}

/// <summary>Midpoints of the frame's outer edges and of structural centrelines.</summary>
public sealed class MidpointSnapProvider : ISnapTargetProvider
{
    public SnapType Type => SnapType.Midpoint;

    public void CollectTargets(SnapScene scene, List<SnapTarget> targets)
    {
        foreach (var frame in scene.Frames)
        {
            foreach (var edge in frame.Bounds.GetEdges())
                targets.Add(SnapTarget.AtPoint(Type, edge.Midpoint));

            var offset = SnapScene.OffsetOf(frame);
            foreach (var profile in scene.StructuralProfiles(frame))
                targets.Add(SnapTarget.AtPoint(Type, profile.Segment.Midpoint + offset));
        }
    }
}

/// <summary>
/// Crossings and T-junctions of structural centrelines, computed with the Core geometry engine
/// (<see cref="Intersection2D"/>); no separate intersection maths lives in the UI.
/// </summary>
public sealed class IntersectionSnapProvider : ISnapTargetProvider
{
    public SnapType Type => SnapType.Intersection;

    public void CollectTargets(SnapScene scene, List<SnapTarget> targets)
    {
        foreach (var frame in scene.Frames)
        {
            var offset = SnapScene.OffsetOf(frame);
            var lines = scene.StructuralProfiles(frame).Select(p => new LineSegment2D(p.StartPoint + offset, p.EndPoint + offset)).ToList();
            for (int i = 0; i < lines.Count; i++)
            for (int j = i + 1; j < lines.Count; j++)
            {
                var hit = Intersection2D.IntersectSegments(lines[i], lines[j]);
                if (hit.Kind == SegmentIntersectionKind.Point && !scene.LiesOnExcludedGeometry(hit.Point))
                    targets.Add(SnapTarget.AtPoint(Type, hit.Point));
            }
        }
    }
}

/// <summary>
/// Centres: of each frame and of each opening. Openings are recomputed WITHOUT the moving divisions, so dragging
/// a mullion offers the centre of the bay it is in, i.e. an equal split.
/// </summary>
public sealed class CenterSnapProvider : ISnapTargetProvider
{
    public SnapType Type => SnapType.Center;

    public void CollectTargets(SnapScene scene, List<SnapTarget> targets)
    {
        foreach (var frame in scene.Frames)
        {
            targets.Add(SnapTarget.AtPoint(Type, frame.Bounds.Center));

            var layoutFrame = frame;
            if (frame.Profiles.Any(p => scene.IsExcluded(p.Id)))
            {
                layoutFrame = FrameSnapshot.Capture(frame).ToFrame();
                layoutFrame.Profiles.RemoveAll(p => scene.IsExcluded(p.Id));
            }

            var layout = FrameLayout.Compute(layoutFrame, scene.Rules);
            if (!layout.IsValid) continue;
            var offset = SnapScene.OffsetOf(frame);
            foreach (var region in layout.Regions)
                targets.Add(SnapTarget.AtPoint(Type, region.CenterlineBounds.Center + offset));
        }
    }
}

/// <summary>Structural edges: the frame's outer edge and inner opening, and every division's faces.</summary>
public sealed class EdgeSnapProvider : ISnapTargetProvider
{
    public SnapType Type => SnapType.Edge;

    public void CollectTargets(SnapScene scene, List<SnapTarget> targets)
    {
        foreach (var frame in scene.Frames)
        {
            var offset = SnapScene.OffsetOf(frame);
            foreach (var edge in frame.Bounds.GetEdges())
                targets.Add(SnapTarget.OnLine(Type, edge));

            if (FrameMembers.Find(frame) is { } outer)
                foreach (var edge in outer.InnerOpening.Offset(offset).GetEdges())
                    targets.Add(SnapTarget.OnLine(Type, edge));

            foreach (var division in frame.Profiles.Where(p => Members.IsDivision(p) && !scene.IsExcluded(p.Id)))
                foreach (var edge in FrameLayout.GetMemberBody(frame, division).Offset(offset).GetEdges())
                    targets.Add(SnapTarget.OnLine(Type, edge));
        }
    }
}

/// <summary>
/// World-grid quantisation. Unlike the geometry providers it has no target list: any position snaps to the
/// nearest grid node/line. It is the lowest-priority snap, used only when no geometry snap applies.
/// </summary>
public sealed class GridSnapProvider
{
    public static double Snap(double value, double spacingMm) => Math.Round(value / spacingMm) * spacingMm;

    public static Point2D Snap(Point2D point, double spacingMm) => new(Snap(point.X, spacingMm), Snap(point.Y, spacingMm));
}
