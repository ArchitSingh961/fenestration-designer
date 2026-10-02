using Mark.Core.Geometry;
using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>Openings (sashes), design templates and design information. Same rules as the other edits:
/// every change runs on a copy, is validated as a whole, and is written back only if valid.</summary>
public static partial class FrameEditor
{
    /// <summary>Longest design reference accepted (it is printed in labels and quote tables).</summary>
    public const int MaxReferenceLength = 30;

    /// <summary>Largest quantity of one design.</summary>
    public const int MaxQuantity = 100_000;

    /// <summary>Highest sill height accepted for the floor line, in mm.</summary>
    public const double MaxFloorDistanceMm = 20_000;

    // ── Openings ────────────────────────────────────────────────────

    /// <summary>
    /// Sets how some openings open. A null <paramref name="opening"/> or <paramref name="mesh"/> keeps each opening's
    /// current value (e.g. to change only the mesh of several different sashes).
    /// </summary>
    public static void SetOpening(Frame frame, IReadOnlyCollection<Guid> glassIds, OpeningType? opening, bool? mesh, DesignRules rules)
        => TrySetOpening(frame, glassIds, opening, mesh, rules).ThrowIfFailed();

    /// <summary>
    /// Sets <see cref="GlassPanel.Opening"/> (and optionally <see cref="GlassPanel.HasMesh"/>) of some of the frame's
    /// panels. Rejected, with nothing changed, if a panel is not part of the frame or an opening is too small for a sash
    /// (<see cref="DesignRules.MinSashOpeningMm"/>).
    /// </summary>
    public static EditResult TrySetOpening(Frame frame, IReadOnlyCollection<Guid> glassIds, OpeningType? opening, bool? mesh,
        DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(glassIds);
        if (opening is { } requested && !Enum.IsDefined(requested))
            return EditResult.Fail("Unknown opening type.");
        if (glassIds.Count == 0)
            return EditResult.Fail("Select an opening (glass panel) first.");
        var ids = glassIds.ToHashSet();
        if (ids.Any(id => frame.GlassPanels.All(g => g.Id != id)))
            return EditResult.Fail("The selected glass panel is not part of this frame.");

        return TryCommit(frame, rules, "Cannot change the opening: ", _ => null, scratch =>
        {
            foreach (var panel in scratch.GlassPanels.Where(g => ids.Contains(g.Id)))
            {
                if (opening is { } o) panel.Opening = o;
                if (mesh is { } m) panel.HasMesh = m;
            }
            return null;
        });
    }

    // ── Design templates ────────────────────────────────────────────

    public static void ApplyTemplate(Frame frame, DesignTemplate template, Guid? targetGlassId, DesignRules rules)
        => TryApplyTemplate(frame, template, targetGlassId, rules).ThrowIfFailed();

    /// <summary>
    /// Applies a library design. With <paramref name="targetGlassId"/> it works inside that one opening (its
    /// surroundings are kept): the opening is split into the template's parts and each part gets its opening type.
    /// Without it, it replaces the whole frame's design: every mullion and transom is removed first, the outer size,
    /// position, profiles' library choices and the glass type stay. A template that only changes the mesh
    /// (<see cref="DesignTemplate.KeepsLayout"/>) never touches divisions; on a frame it changes every opening.
    /// Divisions are placed so the parts' glass sizes follow the template's weights (equal by default).
    /// Rejected as a whole, with nothing changed, if any opening would be too small.
    /// </summary>
    public static EditResult TryApplyTemplate(Frame frame, DesignTemplate template, Guid? targetGlassId, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rules);
        string prefix = $"Cannot apply \"{template.Name}\": ";

        var outer = FrameMembers.Find(frame);
        if (outer is null)
            return EditResult.Fail(prefix + "the frame has no outer profiles.");
        var current = FrameLayout.Compute(frame, rules);
        if (!current.IsValid)
            return EditResult.Fail(prefix + current.Errors[0]);

        GlassPanel? target = null;
        if (targetGlassId is { } glassId)
        {
            target = frame.GlassPanels.FirstOrDefault(g => g.Id == glassId);
            if (target is null)
                return EditResult.Fail(prefix + "the selected glass panel is not part of this frame.");
        }

        if (template.KeepsLayout)
        {
            var leaf = (TemplateLeaf)template.Root;
            var ids = target is null ? frame.GlassPanels.Select(g => g.Id).ToHashSet() : new HashSet<Guid> { target.Id };
            return TryCommit(frame, rules, prefix, _ => null, scratch =>
            {
                foreach (var panel in scratch.GlassPanels.Where(g => ids.Contains(g.Id)))
                    ApplyLeaf(panel, leaf);
                return null;
            });
        }

        Rectangle2D centreline, glass;
        if (target is null)
        {
            centreline = outer.CenterlineLoop;
            glass = outer.InnerOpening;
        }
        else
        {
            if (current.FindByGlass(target.Boundary) is not { } region)
                return EditResult.Fail(prefix + "the selected glass panel no longer matches the frame layout.");
            centreline = region.CenterlineBounds;
            glass = region.GlassBounds;
        }

        var leaves = new List<(TemplateLeaf Leaf, Point2D Centre)>();
        return TryCommit(frame, rules, prefix,
            scratch =>
            {
                if (target is null)
                    scratch.Profiles.RemoveAll(Members.IsDivision);
                return BuildTemplate(scratch, template.Root, centreline, glass, rules, leaves);
            },
            scratch =>
            {
                foreach (var (leaf, centre) in leaves)
                {
                    var panel = scratch.GlassPanels.FirstOrDefault(g => g.Boundary.Contains(centre));
                    if (panel is null)
                        return "the design does not fit this opening.";
                    ApplyLeaf(panel, leaf);
                }
                return null;
            });
    }

    /// <summary>
    /// Adds the divisions of <paramref name="node"/> inside an area given by its centreline and glass (face-to-face)
    /// rectangles, and records each leaf with the centre of its glass. Returns an error message, or null.
    /// </summary>
    private static string? BuildTemplate(Frame frame, TemplateNode node, Rectangle2D centreline, Rectangle2D glass,
        DesignRules rules, List<(TemplateLeaf, Point2D)> leaves)
    {
        if (node is TemplateLeaf leaf)
        {
            leaves.Add((leaf, glass.Center));
            return null;
        }
        if (node is not TemplateSplit split || split.Parts.Count == 0)
            return "the design is empty.";
        if (split.Parts.Count == 1)
            return BuildTemplate(frame, split.Parts[0], centreline, glass, rules, leaves);

        bool vertical = split.Axis == MemberAxis.Vertical;
        int count = split.Parts.Count;
        double thickness = vertical ? rules.MullionThicknessMm : rules.TransomThicknessMm;
        double glassStart = vertical ? glass.Left : glass.Top;
        double glassEnd = vertical ? glass.Right : glass.Bottom;
        double lineStart = vertical ? centreline.Left : centreline.Top;
        double lineEnd = vertical ? centreline.Right : centreline.Bottom;

        var weights = split.EffectiveWeights;
        double total = weights.Sum();
        if (weights.Any(w => !double.IsFinite(w) || w <= 0) || total <= 0)
            return "the design has invalid proportions.";
        double available = glassEnd - glassStart - (count - 1) * thickness;
        if (available <= 0)
            return $"the opening is too small for {count} parts.";

        double cursor = glassStart;
        double previousLine = lineStart;
        for (int i = 0; i < count; i++)
        {
            double partGlassStart = cursor;
            double partGlassEnd = cursor + available * weights[i] / total;
            bool last = i == count - 1;
            double line = last ? lineEnd : partGlassEnd + thickness / 2.0;

            if (!last)
            {
                frame.Profiles.Add(vertical
                    ? new Profile
                    {
                        ProfileType = ProfileType.Mullion,
                        StartPoint = new Point2D(line, centreline.Top),
                        EndPoint = new Point2D(line, centreline.Bottom),
                        Thickness = thickness
                    }
                    : new Profile
                    {
                        ProfileType = ProfileType.Transom,
                        StartPoint = new Point2D(centreline.Left, line),
                        EndPoint = new Point2D(centreline.Right, line),
                        Thickness = thickness
                    });
            }

            var partLine = vertical
                ? Rectangle2D.FromCorners(new Point2D(previousLine, centreline.Top), new Point2D(line, centreline.Bottom))
                : Rectangle2D.FromCorners(new Point2D(centreline.Left, previousLine), new Point2D(centreline.Right, line));
            var partGlass = vertical
                ? Rectangle2D.FromCorners(new Point2D(partGlassStart, glass.Top), new Point2D(partGlassEnd, glass.Bottom))
                : Rectangle2D.FromCorners(new Point2D(glass.Left, partGlassStart), new Point2D(glass.Right, partGlassEnd));

            if (BuildTemplate(frame, split.Parts[i], partLine, partGlass, rules, leaves) is { } error)
                return error;

            previousLine = line;
            cursor = partGlassEnd + thickness;
        }
        return null;
    }

    private static void ApplyLeaf(GlassPanel panel, TemplateLeaf leaf)
    {
        if (leaf.Opening is { } opening) panel.Opening = opening;
        if (leaf.Mesh is { } mesh) panel.HasMesh = mesh;
    }

    /// <summary>
    /// Every openable panel must be big enough for a sash. Panels are numbered top-to-bottom, left-to-right, as on
    /// the drawing. Returns an error message, or null.
    /// </summary>
    internal static string? CheckSashSizes(Frame frame, DesignRules rules)
    {
        for (int i = 0; i < frame.GlassPanels.Count; i++)
        {
            var panel = frame.GlassPanels[i];
            if (!panel.Opening.IsOpenable()) continue;
            var size = panel.Boundary;
            if (size.Width < rules.MinSashOpeningMm - Tol || size.Height < rules.MinSashOpeningMm - Tol)
                return $"opening {i + 1} would be {Members.Format(size.Width)} × {Members.Format(size.Height)} mm, too small " +
                       $"for a {panel.Opening.DisplayName().ToLowerInvariant()} sash (minimum {Members.Format(rules.MinSashOpeningMm)} mm). " +
                       "Make it fixed or larger.";
        }
        return null;
    }

    // ── Design information ──────────────────────────────────────────

    public static void SetDesignInfo(Frame frame, DesignInfo info) => TrySetDesignInfo(frame, info).ThrowIfFailed();

    /// <summary>
    /// Replaces the frame's design information (reference, quantity, location, …) after checking it. Text is
    /// trimmed. Nothing changes if a value is invalid.
    /// </summary>
    public static EditResult TrySetDesignInfo(Frame frame, DesignInfo info)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(info);

        var clean = new DesignInfo
        {
            Reference = (info.Reference ?? "").Trim(),
            Quantity = info.Quantity,
            Name = (info.Name ?? "").Trim(),
            Location = (info.Location ?? "").Trim(),
            Floor = (info.Floor ?? "").Trim(),
            Note = (info.Note ?? "").Trim(),
            FloorDistanceMm = info.FloorDistanceMm
        };

        if (clean.Reference.Length > MaxReferenceLength)
            return EditResult.Fail($"The design reference can be at most {MaxReferenceLength} characters.");
        if (clean.Quantity < 1 || clean.Quantity > MaxQuantity)
            return EditResult.Fail($"The quantity must be a whole number from 1 to {MaxQuantity:N0}.");
        if (clean.FloorDistanceMm is { } floor && (!double.IsFinite(floor) || floor < 0 || floor > MaxFloorDistanceMm))
            return EditResult.Fail($"The floor distance must be between 0 and {Members.Format(MaxFloorDistanceMm)} mm.");

        frame.Design = clean;
        return EditResult.Ok;
    }

    /// <summary>
    /// The next free design reference with <paramref name="prefix"/> in the project: W1, W2, … (the lowest number
    /// not used by any frame).
    /// </summary>
    public static string NextReference(Project project, string prefix = "W")
    {
        ArgumentNullException.ThrowIfNull(project);
        var used = project.Frames.Select(f => f.Design.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int n = 1;
        while (used.Contains(prefix + n)) n++;
        return prefix + n;
    }
}
