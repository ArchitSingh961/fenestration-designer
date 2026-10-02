using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>
/// Box (drag) selection over world geometry.
///
/// Rule, as in most CAD programs:
/// <list type="bullet">
///   <item><b>Window</b> (dragged left → right): selects objects that lie <em>entirely inside</em> the box.</item>
///   <item><b>Crossing</b> (dragged right → left): selects objects that <em>intersect or lie inside</em> the box.</item>
/// </list>
/// The geometry tested is the frame's outer bounds, each division's face-to-face body, and each glass boundary.
/// </summary>
public static class SelectionQuery
{
    public static IReadOnlyList<Guid> InRectangle(Project project, Rectangle2D worldBox, bool crossing)
    {
        ArgumentNullException.ThrowIfNull(project);
        var result = new List<Guid>();

        foreach (var frame in project.Frames)
        {
            if (!worldBox.Intersects(frame.Bounds)) continue;
            var offset = new Vector2D(frame.X, frame.Y);

            if (Matches(frame.Bounds)) result.Add(frame.Id);

            foreach (var profile in frame.Profiles.Where(p => p.ProfileType != ProfileType.Frame))
                if (Matches(FrameLayout.GetMemberBody(frame, profile).Offset(offset)))
                    result.Add(profile.Id);

            foreach (var glass in frame.GlassPanels)
                if (Matches(glass.Boundary.Offset(offset)))
                    result.Add(glass.Id);
        }
        return result;

        bool Matches(Rectangle2D bounds) => crossing ? worldBox.Intersects(bounds) : worldBox.Contains(bounds);
    }

    /// <summary>Ids of every selectable object: frames, their divisions and their glass.</summary>
    public static IReadOnlyList<Guid> All(Project project)
        => project.Frames.SelectMany(f => f.Profiles.Where(p => p.ProfileType != ProfileType.Frame).Select(p => p.Id)
                .Concat(f.GlassPanels.Select(g => g.Id))
                .Prepend(f.Id))
            .ToList();
}
