using Mark.Core.Models;

namespace Mark.Core.Design;

/// <summary>A node of a design template: one opening, or a split into several parts.</summary>
public abstract record TemplateNode
{
    /// <summary>Openings (leaves) side by side across the widest row, and stacked down the tallest column.</summary>
    public abstract (int Columns, int Rows) Grid { get; }
}

/// <summary>
/// One opening of the template. A null <see cref="Opening"/> or <see cref="Mesh"/> keeps the target opening's
/// current value (e.g. "add a mesh shutter" changes only the mesh).
/// </summary>
public sealed record TemplateLeaf(OpeningType? Opening, bool? Mesh = false) : TemplateNode
{
    public override (int Columns, int Rows) Grid => (1, 1);
}

/// <summary>
/// Splits its area into <see cref="Parts"/>: with <see cref="MemberAxis.Vertical"/> divisions (mullions) into
/// columns, left to right; with <see cref="MemberAxis.Horizontal"/> ones (transoms) into rows, top to bottom.
/// <see cref="Weights"/> (optional, one per part) set the relative glass sizes; equal by default.
/// </summary>
public sealed record TemplateSplit(MemberAxis Axis, IReadOnlyList<TemplateNode> Parts, IReadOnlyList<double>? Weights = null)
    : TemplateNode
{
    public override (int Columns, int Rows) Grid
    {
        get
        {
            var grids = Parts.Select(p => p.Grid).ToList();
            return Axis == MemberAxis.Vertical
                ? (grids.Sum(g => g.Columns), grids.Max(g => g.Rows))
                : (grids.Max(g => g.Columns), grids.Sum(g => g.Rows));
        }
    }

    /// <summary>The weight of each part (all 1 when not given).</summary>
    public IReadOnlyList<double> EffectiveWeights
        => Weights is { Count: > 0 } w && w.Count == Parts.Count ? w : Parts.Select(_ => 1.0).ToList();
}

/// <summary>
/// A ready-made window or door layout from the design library, e.g. "2 track 2 panel sliding". Applying it to a
/// frame (or to one opening) builds the mullions/transoms and sets each opening's type; see
/// <see cref="FrameEditor.TryApplyTemplate"/>. Templates are data, not geometry: the sizes come from the target.
/// </summary>
public sealed record DesignTemplate(string Id, string Name, string Category, string Section, TemplateNode Root)
{
    /// <summary>A sensible frame size for a new design of this type (also used for library thumbnails), in mm.</summary>
    public (double Width, double Height) SuggestedSize
    {
        get
        {
            var (columns, rows) = Root.Grid;
            return (Math.Max(1000, 650.0 * columns), Math.Max(1200, 700.0 * rows));
        }
    }

    /// <summary>True for a template that only adds divisions (every opening fixed).</summary>
    public bool IsDividerOnly => Leaves(Root).All(l => l.Opening == OpeningType.Fixed && l.Mesh != true);

    /// <summary>
    /// True for a single opening that keeps the opening type (e.g. add or remove mesh). Applied to a whole frame it
    /// changes every opening and keeps the divisions.
    /// </summary>
    public bool KeepsLayout => Root is TemplateLeaf { Opening: null };

    /// <summary>The opening type of every opening of the design (null: keeps the target's).</summary>
    public IEnumerable<OpeningType?> Openings => Leaves(Root).Select(l => l.Opening);

    internal static IEnumerable<TemplateLeaf> Leaves(TemplateNode node) => node switch
    {
        TemplateLeaf leaf => new[] { leaf },
        TemplateSplit split => split.Parts.SelectMany(Leaves),
        _ => Array.Empty<TemplateLeaf>()
    };
}

/// <summary>
/// The built-in design library, grouped into categories (the side rail) and sections (headings in the panel).
/// Opening directions are as seen from inside.
/// </summary>
public static class DesignTemplates
{
    public const string Dividers = "Dividers";
    public const string Openable = "Openable";
    public const string Sliding = "Sliding";
    public const string Mesh = "Mesh";

    /// <summary>Category names in display order.</summary>
    public static IReadOnlyList<string> Categories { get; } = new[] { Dividers, Openable, Sliding, Mesh };

    private static readonly Lazy<IReadOnlyList<DesignTemplate>> _all = new(Build);

    public static IReadOnlyList<DesignTemplate> All => _all.Value;

    public static DesignTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    public static IEnumerable<DesignTemplate> InCategory(string category) => All.Where(t => t.Category == category);

    /// <summary>
    /// The ready-made designs that suit a product system: those whose openings the system's hardware sets cover (a
    /// sliding system gets sliding designs, a casement system casement and tilt &amp; turn ones), or the openable designs
    /// when the system has no hardware sets; a system without a sash only gets fixed designs. At most
    /// <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<DesignTemplate> ForSystem(Library.ProductSystem system, Library.IProductLibrary library, int max = 12)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(library);
        var covered = library.Bundles.Where(b => b.IsActive && b.IsOpeningSet && (b.SystemId is null || b.SystemId == system.Id))
            .SelectMany(b => b.OpeningTypes).ToHashSet();
        bool sliding = covered.Any(o => o.IsSliding())
                       || (covered.Count == 0 && library.FindProfile(system.FrameProfileId)?.Roles.Contains(ProfileType.Track) == true);
        bool Allowed(OpeningType? opening) => opening is OpeningType.Fixed
            || (system.SashProfileId is not null && opening is { } o
                && (covered.Count > 0 ? covered.Contains(o) : sliding ? o.IsSliding() : o.IsHinged()));
        return All.Where(t => t.Category != Mesh && !t.KeepsLayout && DesignTemplate.Leaves(t.Root).All(l => Allowed(l.Opening)))
            .OrderBy(t => t.IsDividerOnly ? 1 : 0)
            .Take(max)
            .ToList();
    }

    // ── Building blocks ─────────────────────────────────────────────

    private static TemplateLeaf F => new(OpeningType.Fixed);
    private static TemplateLeaf L(OpeningType type, bool mesh = false) => new(type, mesh);

    private static TemplateSplit Columns(params TemplateNode[] parts) => new(MemberAxis.Vertical, parts);
    private static TemplateSplit Rows(params TemplateNode[] parts) => new(MemberAxis.Horizontal, parts);

    private static TemplateSplit Weighted(MemberAxis axis, double[] weights, params TemplateNode[] parts)
        => new(axis, parts, weights);

    private static TemplateSplit Repeat(MemberAxis axis, int count, Func<int, TemplateNode> part)
        => new(axis, Enumerable.Range(0, count).Select(part).ToList());

    private static IReadOnlyList<DesignTemplate> Build()
    {
        var list = new List<DesignTemplate>();
        void Add(string id, string name, string category, string section, TemplateNode root)
            => list.Add(new DesignTemplate(id, name, category, section, root));

        const OpeningType hl = OpeningType.SideHungLeft, hr = OpeningType.SideHungRight;
        const OpeningType sl = OpeningType.SlidingLeft, sr = OpeningType.SlidingRight;

        // ── Dividers ────────────────────────────────────────────────
        Add("div-v2", "Vertical divider", Dividers, "Dividers", Columns(F, F));
        Add("div-h2", "Horizontal divider", Dividers, "Dividers", Rows(F, F));
        Add("div-v3", "Three equal columns", Dividers, "Dividers", Columns(F, F, F));
        Add("div-h3", "Three equal rows", Dividers, "Dividers", Rows(F, F, F));
        Add("div-v4", "Four equal columns", Dividers, "Dividers", Columns(F, F, F, F));
        Add("div-fanlight", "Fanlight over two", Dividers, "Dividers",
            Weighted(MemberAxis.Horizontal, new[] { 1.0, 3.0 }, F, Columns(F, F)));
        Add("div-fixed", "Fixed (clear divisions)", Dividers, "Dividers", F);

        // ── Openable: casement ──────────────────────────────────────
        const string casement = "Casement designs";
        Add("cas-left", "Casement hinged left", Openable, casement, L(hl));
        Add("cas-right", "Casement hinged right", Openable, casement, L(hr));
        Add("cas-top", "Top hung", Openable, casement, L(OpeningType.TopHung));
        Add("cas-bottom", "Bottom hung", Openable, casement, L(OpeningType.BottomHung));
        Add("cas-french", "French casement (pair)", Openable, casement, Columns(L(hl), L(hr)));
        Add("cas-fixed-left", "Fixed + casement", Openable, casement, Columns(F, L(hr)));
        Add("cas-3", "Casement – fixed – casement", Openable, casement, Columns(L(hl), F, L(hr)));
        Add("cas-fanlight", "Casement pair with top-hung fanlight", Openable, casement,
            Weighted(MemberAxis.Horizontal, new[] { 1.0, 3.0 }, L(OpeningType.TopHung), Columns(L(hl), L(hr))));

        // ── Openable: tilt & turn ───────────────────────────────────
        const string tiltTurn = "Tilt & turn designs";
        Add("tt-left", "Tilt & turn hinged left", Openable, tiltTurn, L(OpeningType.TiltTurnLeft));
        Add("tt-right", "Tilt & turn hinged right", Openable, tiltTurn, L(OpeningType.TiltTurnRight));
        Add("tt-pair", "Tilt & turn pair", Openable, tiltTurn, Columns(L(OpeningType.TiltTurnLeft), L(OpeningType.TiltTurnRight)));
        Add("tt-fixed", "Fixed + tilt & turn", Openable, tiltTurn, Columns(F, L(OpeningType.TiltTurnRight)));

        // ── Openable: twin sash (glass + mesh) ──────────────────────
        const string twin = "Twin sash designs (glass + mesh)";
        Add("twin-left", "Twin sash hinged left", Openable, twin, L(hl, mesh: true));
        Add("twin-right", "Twin sash hinged right", Openable, twin, L(hr, mesh: true));
        Add("twin-pair", "Twin sash pair", Openable, twin, Columns(L(hl, true), L(hr, true)));
        Add("twin-top", "Twin sash top hung", Openable, twin, L(OpeningType.TopHung, mesh: true));
        Add("twin-bottom", "Twin sash bottom hung", Openable, twin, L(OpeningType.BottomHung, mesh: true));
        Add("twin-fixed", "Fixed + twin sash", Openable, twin, Columns(F, L(hr, true)));

        // ── Openable: pivot ─────────────────────────────────────────
        const string pivot = "Pivot designs";
        Add("piv-v", "Vertical pivot", Openable, pivot, L(OpeningType.PivotVertical));
        Add("piv-h", "Horizontal pivot", Openable, pivot, L(OpeningType.PivotHorizontal));

        // ── Sliding ─────────────────────────────────────────────────
        const string sliding = "Sliding designs";
        Add("sld-2", "2 track 2 panel", Sliding, sliding, Columns(L(sr), L(sl)));
        Add("sld-3-fixed", "3 panel, fixed centre", Sliding, sliding, Columns(L(sr), F, L(sl)));
        Add("sld-3", "3 track 3 panel", Sliding, sliding, Columns(L(sr), L(sr), L(sl)));
        Add("sld-4", "2 track 4 panel, meeting centre", Sliding, sliding, Columns(L(sr), L(sr), L(sl), L(sl)));
        Add("sld-5", "3 track 5 panel", Sliding, sliding, Repeat(MemberAxis.Vertical, 5, i => L(i < 3 ? sr : sl)));
        Add("sld-6", "3 track 6 panel", Sliding, sliding, Repeat(MemberAxis.Vertical, 6, i => L(i < 3 ? sr : sl)));
        Add("sld-2-mesh", "2 track 2 panel + mesh", Sliding, sliding, Columns(L(sr, true), L(sl, true)));

        const string vertical = "Vertical sliding designs";
        Add("vsl-2", "Vertical slider (2 panel)", Sliding, vertical, Rows(F, L(OpeningType.SlidingUp)));
        Add("vsl-2-down", "Vertical slider, top opens down", Sliding, vertical, Rows(L(OpeningType.SlidingDown), F));
        Add("vsl-3", "Vertical slider (3 panel)", Sliding, vertical,
            Rows(F, L(OpeningType.SlidingUp), L(OpeningType.SlidingUp)));

        const string monorail = "Monorail designs";
        Add("mono-fixed-left", "Fixed + slider", Sliding, monorail, Columns(F, L(sl)));
        Add("mono-fixed-right", "Slider + fixed", Sliding, monorail, Columns(L(sr), F));
        Add("mono-3", "Fixed – slider – fixed", Sliding, monorail, Columns(F, L(sl), F));

        // ── Mesh ────────────────────────────────────────────────────
        const string mesh = "Add-on mesh";
        Add("mesh-add", "Add a mesh shutter (keep the opening)", Mesh, mesh, new TemplateLeaf(null, true));
        Add("mesh-remove", "Remove the mesh shutter", Mesh, mesh, new TemplateLeaf(null, false));
        Add("mesh-left", "Mesh shutter hinged left", Mesh, mesh, L(hl, mesh: true));
        Add("mesh-right", "Mesh shutter hinged right", Mesh, mesh, L(hr, mesh: true));
        Add("mesh-sliding", "Sliding mesh pair", Mesh, mesh, Columns(L(sr, true), L(sl, true)));

        return list;
    }
}
