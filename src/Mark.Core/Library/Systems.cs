using Mark.Core.Models;

namespace Mark.Core.Library;

/// <summary>What a system or product is made for.</summary>
public enum ProductUse
{
    WindowAndDoor,
    Window,
    Door
}

/// <summary>The material of a product system. Each is licensed separately (uPVC / Aluminium).</summary>
public enum SystemMaterial
{
    Upvc,
    Aluminium
}

/// <summary>
/// What a library item is used with: the systems (empty = any system), the opening types (empty = any) and windows,
/// doors or both. Pickers offer an item only where it fits; the owner's catalogue sends it with those systems.
/// </summary>
public sealed record UsedWith
{
    public IReadOnlyList<string> SystemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<OpeningType> OpeningTypes { get; init; } = Array.Empty<OpeningType>();

    public ProductUse Use { get; init; } = ProductUse.WindowAndDoor;

    /// <summary>True when the item may be used in <paramref name="systemId"/> (any item fits a frame without a system).</summary>
    public bool FitsSystem(string? systemId) => SystemIds.Count == 0 || systemId is null || SystemIds.Contains(systemId);

    public bool FitsOpening(OpeningType opening) => OpeningTypes.Count == 0 || OpeningTypes.Contains(opening);
}

/// <summary>
/// The steel (or other) reinforcement inside a profile: which section, from which bar length on, and how much shorter
/// than the bar it is cut. It is listed, cut and priced with the bar it reinforces.
/// </summary>
public sealed record ReinforcementRule
{
    /// <summary>The reinforcement section, a library profile with the <see cref="ProfileType.Reinforcement"/> role.</summary>
    public string ProfileId { get; init; } = "";

    /// <summary>Only bars at least this long are reinforced; 0 = every bar.</summary>
    public double MinLengthMm { get; init; }

    /// <summary>The reinforcement is cut this much shorter than the bar.</summary>
    public double CutDeductionMm { get; init; }
}

/// <summary>
/// A product system, e.g. "62mm Casement – uPVC" or "Series 60 Sliding – Aluminium": its material and the profiles and
/// glass a design in it uses by default (members without an explicit profile take these), and the glass it accepts.
/// </summary>
public sealed record ProductSystem
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public SystemMaterial Material { get; init; } = SystemMaterial.Aluminium;

    public ProductUse Use { get; init; } = ProductUse.Window;

    public string? Description { get; init; }

    public string? FrameProfileId { get; init; }

    public string? MullionProfileId { get; init; }

    public string? TransomProfileId { get; init; }

    public string? SashProfileId { get; init; }

    public string? MeshSashProfileId { get; init; }

    public string? GlassId { get; init; }

    /// <summary>The thinnest glass (or unit) the system takes; 0 = no limit.</summary>
    public double GlassMinThicknessMm { get; init; }

    /// <summary>The thickest glass the system takes; 0 = no limit.</summary>
    public double GlassMaxThicknessMm { get; init; }

    public bool IsActive { get; init; } = true;

    public string? ProfileIdFor(ProfileType role) => role switch
    {
        ProfileType.Frame => FrameProfileId,
        ProfileType.Mullion => MullionProfileId,
        ProfileType.Transom => TransomProfileId,
        ProfileType.Sash => SashProfileId,
        ProfileType.MeshSash => MeshSashProfileId,
        _ => null
    };

    /// <summary>True when glass of this thickness fits the system.</summary>
    public bool AcceptsGlass(double thicknessMm)
        => (GlassMinThicknessMm <= 0 || thicknessMm >= GlassMinThicknessMm - 1e-6)
           && (GlassMaxThicknessMm <= 0 || thicknessMm <= GlassMaxThicknessMm + 1e-6);

    /// <summary>"20–28 mm", "up to 10 mm", or "" without a limit.</summary>
    public string GlassRangeText
        => (GlassMinThicknessMm, GlassMaxThicknessMm) switch
        {
            ( <= 0, <= 0) => "",
            ( <= 0, var max) => $"up to {max:0.##} mm",
            (var min, <= 0) => $"from {min:0.##} mm",
            var (min, max) => $"{min:0.##}–{max:0.##} mm"
        };
}

/// <summary>How a bundle part's quantity is worked out.</summary>
public enum PartBasis
{
    /// <summary>A fixed number per bar (member bundles) or per opening (opening sets).</summary>
    PerPiece,

    /// <summary>Per metre of the bar (member bundles) or of the measure (opening sets).</summary>
    PerMetre,

    /// <summary>By size: the first step whose "up to" covers the measure (e.g. 2 hinges up to 1200 mm, 3 above).</summary>
    BySize
}

/// <summary>What a part is measured by: the bar's length, or the opening's width, height or longest side.</summary>
public enum SizeMeasure
{
    /// <summary>Member bundles: the bar's cut length. Opening sets: the opening's perimeter.</summary>
    Length,
    Width,
    Height,
    LongestSide
}

/// <summary>Which bars of a member a part goes with (e.g. the track rail only on the bottom frame bar).</summary>
public enum BarSide
{
    Any,
    Horizontal,
    Vertical,
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>A quantity for sizes up to <see cref="UpToMm"/> (bigger sizes use the next step, or the last one).</summary>
public sealed record SizeStep
{
    public double UpToMm { get; init; }

    public double Quantity { get; init; }
}

/// <summary>
/// One part of a bundle: a profile (cut to size and added to the cutting list) or a material (hardware, gasket,
/// accessory), with how many go with each bar or opening.
/// </summary>
public sealed record BundlePart
{
    /// <summary>A profile or material id.</summary>
    public string ItemId { get; init; } = "";

    public PartBasis Basis { get; init; } = PartBasis.PerPiece;

    /// <summary>The count (per piece), the metres per metre (per metre); unused by size.</summary>
    public double Quantity { get; init; } = 1;

    public SizeMeasure Measure { get; init; } = SizeMeasure.Length;

    public BarSide Side { get; init; } = BarSide.Any;

    /// <summary>Profile parts: the part is cut this much shorter than its measure.</summary>
    public double CutDeductionMm { get; init; }

    /// <summary>By size: the steps, smallest "up to" first.</summary>
    public IReadOnlyList<SizeStep> Steps { get; init; } = Array.Empty<SizeStep>();

    /// <summary>The quantity for a measure of <paramref name="sizeMm"/> (by size), else <see cref="Quantity"/>.</summary>
    public double QuantityFor(double sizeMm)
    {
        if (Basis != PartBasis.BySize) return Quantity;
        if (Steps.Count == 0) return 0;
        foreach (var step in Steps)
            if (sizeMm <= step.UpToMm + 1e-6) return step.Quantity;
        return Steps[^1].Quantity;
    }
}

/// <summary>
/// Parts that always go together. A <b>member bundle</b> (<see cref="ProfileId"/> set) goes with every bar made from
/// that profile, e.g. "Sliding 2-track frame": track rail and bottom cover on the bottom bar, gasket on all bars.
/// An <b>opening set</b> (no profile) goes with every opening of the listed types, e.g. "Casement hardware": hinges by
/// height, handle, lock. A bundle with a <see cref="SystemId"/> applies only to designs in that system.
/// </summary>
public sealed record Bundle
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string? SystemId { get; init; }

    /// <summary>The main profile of a member bundle; null for an opening set.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Opening types it applies to (sash bars and opening sets); empty = every opening type (sets: every openable one).</summary>
    public IReadOnlyList<OpeningType> OpeningTypes { get; init; } = Array.Empty<OpeningType>();

    public IReadOnlyList<BundlePart> Parts { get; init; } = Array.Empty<BundlePart>();

    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;

    public bool IsOpeningSet => ProfileId is null;

    public bool AppliesToSystem(string? systemId) => SystemId is null || SystemId == systemId;

    /// <summary>Opening sets: whether an opening of this type gets the set.</summary>
    public bool AppliesToOpening(OpeningType opening)
        => OpeningTypes.Count == 0 ? opening != OpeningType.Fixed : OpeningTypes.Contains(opening);
}

/// <summary>What the owner gives a company: whole systems (with everything they use) and further single items.</summary>
public sealed record CatalogueSelection
{
    public IReadOnlyList<string> SystemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ItemIds { get; init; } = Array.Empty<string>();

    public bool IsEmpty => SystemIds.Count == 0 && ItemIds.Count == 0;
}
