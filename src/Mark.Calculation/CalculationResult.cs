using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Calculation;

public enum IssueSeverity
{
    /// <summary>The result is usable but something looks wrong (e.g. drawn width differs from the library).</summary>
    Warning,

    /// <summary>Part of the design could not be priced (e.g. a reference to a product not in the library).</summary>
    Error
}

/// <param name="ObjectId">The frame, profile or glass panel concerned, so the UI can point at it.</param>
public sealed record CalculationIssue(IssueSeverity Severity, string Message, Guid? ObjectId = null);

/// <summary>
/// One glass pane. Sizes are the manufacturing size: the face-to-face opening plus the glazing bite of the
/// surrounding profiles, minus the edge clearance.
/// </summary>
public sealed record GlassLine
{
    public Guid FrameId { get; init; }
    public Guid GlassPanelId { get; init; }

    /// <summary>The definition used (the panel's own, or the library default), or the unresolved id.</summary>
    public string? DefinitionId { get; init; }
    public string Name { get; init; } = "";
    public string? Category { get; init; }

    /// <summary>False if no library glass could be found: the line is not priced and not in the BOM.</summary>
    public bool IsResolved { get; init; }

    /// <summary>True if the panel has no reference of its own and the library default was used.</summary>
    public bool IsDefault { get; init; }

    public double ThicknessMm { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }

    /// <summary>Panes needed: the design's quantity. Area, weight and cost on this line are for ONE pane.</summary>
    public int Quantity { get; init; } = 1;
    public double AreaM2 { get; init; }

    /// <summary>The area charged for (at least the library's minimum chargeable area).</summary>
    public double ChargeableAreaM2 { get; init; }
    public double PerimeterM { get; init; }
    public double? WeightKg { get; init; }
    public decimal CostPerSquareMetre { get; init; }
    public decimal Cost { get; init; }
}

/// <summary>
/// One profile piece to cut: a member of the design, or a bar of a sash or mesh shutter (then <see cref="ProfileId"/> is
/// empty and <see cref="OpeningId"/> is the glass panel the sash is in).
/// </summary>
public sealed record ProfileLine
{
    public Guid FrameId { get; init; }
    public Guid ProfileId { get; init; }

    /// <summary>For sash and mesh bars: the opening (glass panel) they belong to; null for design members.</summary>
    public Guid? OpeningId { get; init; }
    public ProfileType Role { get; init; }
    public string? DefinitionId { get; init; }
    public string Name { get; init; } = "";
    public bool IsResolved { get; init; }
    public bool IsDefault { get; init; }

    /// <summary>Length to cut (long point to long point for mitres).</summary>
    public double CutLengthMm { get; init; }

    /// <summary>Cut angle at each end in degrees: 90 = square, 45 = mitre.</summary>
    public double StartCutAngle { get; init; } = 90;
    public double EndCutAngle { get; init; } = 90;

    /// <summary>Pieces needed: the design's quantity. Weight and cost on this line are for ONE piece.</summary>
    public int Quantity { get; init; } = 1;
    public double WeightKg { get; init; }
    public decimal CostPerMetre { get; init; }
    public decimal Cost { get; init; }

    /// <summary>
    /// For a part that goes with a bar or opening (a bundle part or reinforcement): what it is part of, e.g.
    /// "Sliding 2-track frame" or "Reinforcement". Null for the design's own members and sash bars.
    /// </summary>
    public string? PartOf { get; init; }
}

/// <summary>Material consumed by one profile piece or glass pane, from a usage rule in its definition.</summary>
public sealed record MaterialLine
{
    public Guid FrameId { get; init; }

    /// <summary>The profile or glass panel that consumes it.</summary>
    public Guid SourceId { get; init; }
    public string MaterialId { get; init; } = "";
    public string Name { get; init; } = "";
    public MaterialCategory Category { get; init; }
    public MaterialUnit Unit { get; init; }

    /// <summary>Amount for ONE window; <see cref="Cost"/> likewise.</summary>
    public double Quantity { get; init; }
    public decimal Cost { get; init; }

    /// <summary>How many windows use it: the design's quantity.</summary>
    public int Windows { get; init; } = 1;
}

/// <summary>
/// An opening of a window as the pricing needs it: its type (hardware), whether it has a sash, and the mesh area.
/// Values are for ONE window; <see cref="Windows"/> is the design's quantity.
/// </summary>
/// <param name="HasHardwareSet">The library's opening sets (hardware) already priced this opening's hardware.</param>
public sealed record OpeningLine(Guid FrameId, Guid GlassPanelId, OpeningType Opening, bool HasSash, bool HasMesh,
    double MeshAreaM2, int Windows, bool HasHardwareSet = false);

/// <summary>Identical pieces of one profile, grouped for the saw (input for cutting optimisation).</summary>
public sealed record CutListLine(string DefinitionId, string Name, double CutLengthMm, double StartCutAngle,
    double EndCutAngle, int Quantity, double StockLengthMm);

public enum BomCategory
{
    Profile,
    Glass,
    Hardware,
    Gasket,
    Accessory,
    Consumable,

    /// <summary>Reinforcement sections (steel inside uPVC profiles).</summary>
    Reinforcement
}

/// <summary>One aggregated Bill of Materials row.</summary>
/// <param name="Description">What distinguishes rows of the same item, e.g. the glass size.</param>
/// <param name="Quantity">Pieces/panes for profiles and glass; the material's unit otherwise.</param>
/// <param name="LengthMm">Profiles: total cut length.</param>
/// <param name="AreaM2">Glass: total area.</param>
public sealed record BomLine(BomCategory Category, string ItemId, string Name, string Description, double Quantity,
    string Unit, double? LengthMm, double? AreaM2, double? WeightKg, decimal Cost);

public sealed record CostSummary(decimal Profiles, decimal Glass, decimal Materials)
{
    public decimal Total => Profiles + Glass + Materials;

    public static CostSummary Zero { get; } = new(0, 0, 0);

    public CostSummary Add(CostSummary other) => new(Profiles + other.Profiles, Glass + other.Glass, Materials + other.Materials);
}

/// <summary>
/// One design. <see cref="Cost"/> and <see cref="WeightKg"/> are for ONE window; <see cref="Quantity"/> windows are needed.
/// </summary>
public sealed record FrameCalculation(Guid FrameId, double WidthMm, double HeightMm, CostSummary Cost, double WeightKg)
{
    public int Quantity { get; init; } = 1;

    /// <summary>Total metres of profile in one window (frame, divisions, sash and mesh bars).</summary>
    public double ProfileMetres { get; init; }

    /// <summary>Glass area of one window in m².</summary>
    public double GlassAreaM2 { get; init; }

    /// <summary>Metres of library reinforcement in one window (0 when the library has none for its profiles).</summary>
    public double ReinforcementMetres { get; init; }

    /// <summary>Outer area of one window in m².</summary>
    public double AreaM2 => WidthMm * HeightMm / 1_000_000.0;
}

/// <summary>
/// Everything the calculation engine derives from one design + library + rules. Immutable; lines are in design
/// order (frames, then each frame's profiles and glass); BOM and cut list are sorted, so equal inputs give equal results.
/// Every line carries the Ids of the design objects it came from. Lines are per window; the BOM, cut list, total
/// <see cref="Cost"/> and <see cref="WeightKg"/> include every window of every design (its quantity).
/// </summary>
public sealed class CalculationResult
{
    public CalculationResult(string currency, IReadOnlyList<FrameCalculation> frames, IReadOnlyList<ProfileLine> profiles,
        IReadOnlyList<GlassLine> glass, IReadOnlyList<MaterialLine> materials, IReadOnlyList<CutListLine> cutList,
        IReadOnlyList<BomLine> bom, CostSummary cost, double weightKg, IReadOnlyList<CalculationIssue> issues,
        IReadOnlyList<OpeningLine>? openings = null)
    {
        Openings = openings ?? Array.Empty<OpeningLine>();
        Currency = currency;
        Frames = frames;
        Profiles = profiles;
        Glass = glass;
        Materials = materials;
        CutList = cutList;
        Bom = bom;
        Cost = cost;
        WeightKg = weightKg;
        Issues = issues;
    }

    public static CalculationResult Empty { get; } = new("", Array.Empty<FrameCalculation>(), Array.Empty<ProfileLine>(),
        Array.Empty<GlassLine>(), Array.Empty<MaterialLine>(), Array.Empty<CutListLine>(), Array.Empty<BomLine>(),
        CostSummary.Zero, 0, Array.Empty<CalculationIssue>());

    public string Currency { get; }
    public IReadOnlyList<FrameCalculation> Frames { get; }
    public IReadOnlyList<ProfileLine> Profiles { get; }
    public IReadOnlyList<GlassLine> Glass { get; }
    public IReadOnlyList<MaterialLine> Materials { get; }
    public IReadOnlyList<CutListLine> CutList { get; }
    public IReadOnlyList<BomLine> Bom { get; }
    public CostSummary Cost { get; }
    public double WeightKg { get; }
    public IReadOnlyList<CalculationIssue> Issues { get; }

    /// <summary>Every opening of every design (for hardware and mesh pricing).</summary>
    public IReadOnlyList<OpeningLine> Openings { get; }

    /// <summary>True when every item was priced from the library (no errors; warnings allowed).</summary>
    public bool IsComplete => Issues.All(i => i.Severity != IssueSeverity.Error);

    public GlassLine? FindGlass(Guid glassPanelId) => Glass.FirstOrDefault(g => g.GlassPanelId == glassPanelId);

    public ProfileLine? FindProfile(Guid profileId) => Profiles.FirstOrDefault(p => p.OpeningId is null && p.ProfileId == profileId);

    /// <summary>The sash and mesh bars of one opening.</summary>
    public IEnumerable<ProfileLine> SashBarsOf(Guid glassPanelId) => Profiles.Where(p => p.OpeningId == glassPanelId);

    public FrameCalculation? FindFrame(Guid frameId) => Frames.FirstOrDefault(f => f.FrameId == frameId);
}
