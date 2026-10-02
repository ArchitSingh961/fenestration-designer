namespace Mark.Core.Models;

/// <summary>What a cost head's rate is multiplied by. Per-window bases are per piece of a design (then × quantity).</summary>
public enum CostBasis
{
    /// <summary>Rate % of the profile cost (frame, mullions, transoms, sash and mesh bars).</summary>
    PercentOfProfiles,

    /// <summary>Rate % of the glass cost.</summary>
    PercentOfGlass,

    /// <summary>Rate % of the accessories cost (gaskets, cleats, screws… from the library usages).</summary>
    PercentOfAccessories,

    /// <summary>Rate % of the raw material cost (profiles + glass + accessories + hardware + mesh).</summary>
    PercentOfMaterials,

    /// <summary>Rate % of everything before this head (materials and the heads above it).</summary>
    PercentOfRunningTotal,

    /// <summary>Rate per metre of profile (all bars, e.g. powder coating or reinforcement).</summary>
    PerMetreOfProfile,

    /// <summary>Rate per m² of window (outer width × height), e.g. fabrication or installation labour.</summary>
    PerSquareMetreOfWindow,

    /// <summary>Rate per m² of glass.</summary>
    PerSquareMetreOfGlass,

    /// <summary>Rate per window (piece).</summary>
    PerWindow,

    /// <summary>Rate per openable sash.</summary>
    PerSash,

    /// <summary>A fixed amount for the whole quote (charges only, e.g. transport).</summary>
    FixedPerQuote
}

/// <summary>One line of the price structure, e.g. "Profile wastage — 10 % of profiles".</summary>
public sealed class CostHead
{
    public string Name { get; set; } = "";
    public CostBasis Basis { get; set; } = CostBasis.PercentOfMaterials;

    /// <summary>A percentage for the "PercentOf…" bases, otherwise an amount in the library's currency.</summary>
    public decimal Rate { get; set; }

    /// <summary>Shown as its own line on the quote (otherwise folded into the price).</summary>
    public bool ShowOnQuote { get; set; }

    public CostHead Copy() => (CostHead)MemberwiseClone();

    public static bool IsPercent(CostBasis basis) => basis is CostBasis.PercentOfProfiles or CostBasis.PercentOfGlass
        or CostBasis.PercentOfAccessories or CostBasis.PercentOfMaterials or CostBasis.PercentOfRunningTotal;
}

/// <summary>Rates for things the product library does not price: hardware per sash, mesh, reinforcement.</summary>
public sealed class PriceRates
{
    /// <summary>Hardware set (hinges, handle, stays) per side-hung casement sash.</summary>
    public decimal CasementHardware { get; set; }

    /// <summary>Hardware set per top-hung or bottom-hung sash.</summary>
    public decimal HungHardware { get; set; }

    /// <summary>Tilt &amp; turn gear per sash.</summary>
    public decimal TiltTurnHardware { get; set; }

    /// <summary>Pivot hinges and handle per sash.</summary>
    public decimal PivotHardware { get; set; }

    /// <summary>Rollers, lock and handle per sliding panel.</summary>
    public decimal SlidingHardware { get; set; }

    /// <summary>Insect mesh (fabric and fixing) per m² of mesh.</summary>
    public decimal MeshPerSquareMetre { get; set; }

    /// <summary>Steel reinforcement per metre of profile (uPVC); 0 for aluminium.</summary>
    public decimal ReinforcementPerMetre { get; set; }

    /// <summary>The hardware rate for one sash of <paramref name="type"/> (0 for fixed glass).</summary>
    public decimal HardwareFor(OpeningType type) => type switch
    {
        OpeningType.SideHungLeft or OpeningType.SideHungRight => CasementHardware,
        OpeningType.TopHung or OpeningType.BottomHung => HungHardware,
        OpeningType.TiltTurnLeft or OpeningType.TiltTurnRight => TiltTurnHardware,
        OpeningType.PivotVertical or OpeningType.PivotHorizontal => PivotHardware,
        OpeningType.Fixed => 0,
        _ => SlidingHardware
    };

    public PriceRates Copy() => (PriceRates)MemberwiseClone();
}

/// <summary>
/// How a quote is priced, on top of the product library's material prices:
/// <code>
/// material cost (library) + hardware + mesh + reinforcement      per window
///   + cost heads, in order                                       per window   → basic price
/// basic value (all windows × quantities) − discount %            → sub-total
///   + charges (transport, loading…)                              → total
///   + tax %                                                      → grand total
/// </code>
/// A company keeps one as its default; each quote gets its own copy, which can be changed for that quote only.
/// </summary>
public sealed class PriceStructure
{
    /// <summary>A name for the structure, e.g. "Retail" or "Builder projects".</summary>
    public string Name { get; set; } = "Retail";

    /// <summary>Added to each window's price, in order (a running-total percentage sees the ones above it).</summary>
    public List<CostHead> Heads { get; set; } = new();

    public decimal DiscountPercent { get; set; }

    /// <summary>Added after the discount: fixed per quote, per window or per m² of window (e.g. transport, loading).</summary>
    public List<CostHead> Charges { get; set; } = new();

    public string TaxName { get; set; } = "GST";

    public decimal TaxPercent { get; set; }

    public PriceRates Rates
    {
        get => _rates;
        set => _rates = value ?? new PriceRates();
    }

    private PriceRates _rates = new();

    public PriceStructure Copy() => new()
    {
        Name = Name,
        Heads = Heads.Select(h => h.Copy()).ToList(),
        DiscountPercent = DiscountPercent,
        Charges = Charges.Select(c => c.Copy()).ToList(),
        TaxName = TaxName,
        TaxPercent = TaxPercent,
        Rates = Rates.Copy()
    };

    /// <summary>A sensible starting point for an aluminium window workshop in India (rates in the library currency).</summary>
    public static PriceStructure Default() => new()
    {
        Name = "Retail",
        Heads =
        {
            new CostHead { Name = "Profile wastage", Basis = CostBasis.PercentOfProfiles, Rate = 10 },
            new CostHead { Name = "Glass wastage", Basis = CostBasis.PercentOfGlass, Rate = 5 },
            new CostHead { Name = "Powder coating", Basis = CostBasis.PerMetreOfProfile, Rate = 60 },
            new CostHead { Name = "Fabrication labour", Basis = CostBasis.PerSquareMetreOfWindow, Rate = 750 },
            new CostHead { Name = "Installation labour", Basis = CostBasis.PerSquareMetreOfWindow, Rate = 500 },
            new CostHead { Name = "Overheads and margin", Basis = CostBasis.PercentOfRunningTotal, Rate = 20 }
        },
        DiscountPercent = 0,
        Charges =
        {
            new CostHead { Name = "Transportation", Basis = CostBasis.FixedPerQuote, Rate = 1000, ShowOnQuote = true },
            new CostHead { Name = "Loading and unloading", Basis = CostBasis.FixedPerQuote, Rate = 1000, ShowOnQuote = true }
        },
        TaxName = "GST",
        TaxPercent = 18,
        Rates = new PriceRates
        {
            CasementHardware = 1200,
            HungHardware = 950,
            TiltTurnHardware = 3500,
            PivotHardware = 2800,
            SlidingHardware = 850,
            MeshPerSquareMetre = 450,
            ReinforcementPerMetre = 0
        }
    };
}
