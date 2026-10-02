using Fenestration.Core.Models;

namespace Fenestration.Core.Library;

/// <summary>
/// A profile (bar) product from the library: an aluminium or uPVC section that frames, mullions or transoms
/// are cut from. The design references it by <see cref="Id"/> (<see cref="Profile.ProfileDefinitionId"/>);
/// everything product-specific (size, weight, price, cutting data) lives here, never in code.
/// Lengths are millimetres, weights kilograms, prices in the library's currency.
/// </summary>
public sealed record ProfileDefinition
{
    /// <summary>Stable library code, e.g. "PRF-FRAME-60". Never reused for a different product.</summary>
    public string Id { get; init; } = "";

    /// <summary>Display name, e.g. "60mm Frame".</summary>
    public string Name { get; init; } = "";

    /// <summary>Manufacturer's article number, if different from <see cref="Id"/>.</summary>
    public string? Code { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>Profile system / series name (searchable).</summary>
    public string? Series { get; init; }

    /// <summary>The roles this section can be used for (e.g. Frame, or Mullion and Transom).</summary>
    public IReadOnlyList<ProfileType> Roles { get; init; } = Array.Empty<ProfileType>();

    /// <summary>Visible face width. Assigning the profile sets the member's drawn thickness to this.</summary>
    public double FaceWidthMm { get; init; }

    /// <summary>Section depth (front to back). Informational.</summary>
    public double DepthMm { get; init; }

    public double WeightKgPerMetre { get; init; }

    public decimal CostPerMetre { get; init; }

    /// <summary>Length of the stock bars the section is supplied in (0 = unknown). Used by cutting optimisation.</summary>
    public double StockLengthMm { get; init; }

    /// <summary>
    /// Further stock lengths the section is available in (e.g. 6000, 6500, 7000). Optional: merged with
    /// <see cref="StockLengthMm"/>, see <see cref="AvailableStockLengthsMm"/>.
    /// </summary>
    public IReadOnlyList<double> StockLengthsMm { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Added to EACH square-cut end of a mullion/transom beyond its face-to-face length (e.g. the part that
    /// sits in the frame rebate). Negative values are a deduction.
    /// </summary>
    public double CutAllowancePerEndMm { get; init; }

    /// <summary>How far the glass edge sits inside this profile's face (glazing bite), per side.</summary>
    public double GlazingBiteMm { get; init; }

    /// <summary>Accessories consumed per piece or per metre of this profile (gaskets, cleats, connectors…).</summary>
    public IReadOnlyList<MaterialUsage> Materials { get; init; } = Array.Empty<MaterialUsage>();

    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// False once the product is retired: it is no longer offered for new assignments (searches skip it unless
    /// <see cref="LibraryQuery.IncludeInactive"/>), but it still resolves by Id, so existing designs keep pricing.
    /// </summary>
    public bool IsActive { get; init; } = true;

    public bool Supports(ProfileType role) => Roles.Contains(role);

    /// <summary>
    /// Every stock length the section can be bought in: <see cref="StockLengthMm"/> (if set) and
    /// <see cref="StockLengthsMm"/>, without duplicates, shortest first. Empty if the library gives none.
    /// (A method, not a property, so it is never written to the library file.)
    /// </summary>
    public IReadOnlyList<double> AvailableStockLengthsMm()
        => (StockLengthsMm ?? Array.Empty<double>()).Append(StockLengthMm)
            .Where(length => length > 0)
            .Distinct()
            .OrderBy(length => length)
            .ToList();
}

/// <summary>A glass product from the library (referenced by <see cref="GlassPanel.GlassDefinitionId"/>).</summary>
public sealed record GlassDefinition
{
    /// <summary>Stable library code, e.g. "GLS-TGH-8".</summary>
    public string Id { get; init; } = "";

    /// <summary>Display name, e.g. "8mm Toughened".</summary>
    public string Name { get; init; } = "";

    public string? Code { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>Free-text type/category from the library, e.g. "Toughened", "Laminated", "Insulated" (searchable).</summary>
    public string? Category { get; init; }

    /// <summary>Total glass (or unit) thickness.</summary>
    public double ThicknessMm { get; init; }

    public decimal CostPerSquareMetre { get; init; }

    /// <summary>Weight per m², if the library provides it.</summary>
    public double? WeightKgPerSquareMetre { get; init; }

    /// <summary>Smaller panes are charged as this area (a common glass-pricing rule). 0 = no minimum.</summary>
    public double MinChargeableAreaM2 { get; init; }

    /// <summary>Accessories consumed per pane, per metre of perimeter or per m² (glazing gasket, setting blocks…).</summary>
    public IReadOnlyList<MaterialUsage> Materials { get; init; } = Array.Empty<MaterialUsage>();

    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>False once retired: not offered for new assignments, still resolvable by Id (see ProfileDefinition).</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>What kind of BOM item a <see cref="MaterialDefinition"/> is. Categories, not products.</summary>
public enum MaterialCategory
{
    Hardware,
    Gasket,
    Accessory,
    Consumable
}

/// <summary>The unit a material is counted and priced in.</summary>
public enum MaterialUnit
{
    Piece,
    Metre,
    SquareMetre
}

/// <summary>Any other library item: hardware, gaskets, accessories, consumables.</summary>
public sealed record MaterialDefinition
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string? Code { get; init; }

    public string? Manufacturer { get; init; }

    public MaterialCategory Category { get; init; } = MaterialCategory.Accessory;

    public MaterialUnit Unit { get; init; } = MaterialUnit.Piece;

    public decimal CostPerUnit { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>False once retired: not offered for new usages, still resolvable by Id (see ProfileDefinition).</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>What a <see cref="MaterialUsage.Quantity"/> is multiplied by.</summary>
public enum UsageBasis
{
    /// <summary>Per profile piece, or per glass pane.</summary>
    PerPiece,

    /// <summary>Per metre of profile cut length, or per metre of glass perimeter.</summary>
    PerMetre,

    /// <summary>Per m² of glass (glass only).</summary>
    PerSquareMetre
}

/// <summary>A rule in a profile or glass definition: "uses <see cref="Quantity"/> of material X per basis".</summary>
public sealed record MaterialUsage
{
    public string MaterialId { get; init; } = "";

    public UsageBasis Basis { get; init; } = UsageBasis.PerPiece;

    public double Quantity { get; init; }
}

/// <summary>
/// The library's defaults: what a design object uses while it has no explicit reference (e.g. a newly drawn
/// mullion). Defaults are library data, so a different library (another profile system) changes them.
/// </summary>
public sealed record LibraryDefaults
{
    public string? FrameProfileId { get; init; }

    public string? MullionProfileId { get; init; }

    public string? TransomProfileId { get; init; }

    public string? GlassId { get; init; }

    public string? ProfileIdFor(ProfileType role) => role switch
    {
        ProfileType.Frame => FrameProfileId,
        ProfileType.Mullion => MullionProfileId,
        ProfileType.Transom => TransomProfileId,
        _ => null
    };
}
