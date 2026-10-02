using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Snapping;

/// <summary>
/// The geometry snap providers may read during one interaction, with the object(s) being edited taken out.
///
/// Excluding an object also excludes geometry that <em>depends</em> on it, because that geometry moves with the
/// object and would be a stale target:
/// <list type="bullet">
///   <item>an excluded frame removes everything in it;</item>
///   <item>an excluded division also removes the divisions that end on it, and any point lying on its centreline
///         (e.g. its intersections).</item>
/// </list>
/// </summary>
public sealed class SnapScene
{
    private readonly HashSet<Guid> _excluded;
    private readonly List<LineSegment2D> _excludedCenterlines = new();

    public SnapScene(Project project, IEnumerable<Guid>? excludeIds, DesignRules rules)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _excluded = excludeIds?.ToHashSet() ?? new HashSet<Guid>();

        foreach (var frame in project.Frames)
        {
            if (_excluded.Contains(frame.Id)) continue;
            var offset = new Vector2D(frame.X, frame.Y);
            foreach (var profile in frame.Profiles.Where(p => _excluded.Contains(p.Id)).ToList())
            {
                _excludedCenterlines.Add(new LineSegment2D(profile.StartPoint + offset, profile.EndPoint + offset));
                foreach (var dependent in FrameLayout.GetMembersEndingOn(frame, profile))
                    if (Members.IsDivision(dependent))
                        _excluded.Add(dependent.Id);
            }
        }
    }

    public Project Project { get; }

    public DesignRules Rules { get; }

    /// <summary>Frames that may provide targets.</summary>
    public IEnumerable<Frame> Frames => Project.Frames.Where(f => !_excluded.Contains(f.Id));

    public bool IsExcluded(Guid id) => _excluded.Contains(id);

    /// <summary>True if a world point sits on the centreline of an excluded (moving) profile.</summary>
    public bool LiesOnExcludedGeometry(Point2D worldPoint)
        => _excludedCenterlines.Any(line => line.DistanceTo(worldPoint) <= GeometryTolerance.Default);

    /// <summary>Structural profiles of <paramref name="frame"/> that may provide targets.</summary>
    public IEnumerable<Profile> StructuralProfiles(Frame frame)
        => frame.Profiles.Where(p => Members.IsStructural(p) && !_excluded.Contains(p.Id));

    public static Vector2D OffsetOf(Frame frame) => new(frame.X, frame.Y);
}
