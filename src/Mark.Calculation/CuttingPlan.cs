namespace Mark.Calculation;

/// <summary>
/// One stock bar and the pieces cut from it. Lengths in mm. Every bar balances exactly:
/// <c>StockLengthMm = CutLengthMm + RemnantMm + WasteMm</c>, where waste is the trim, the saw kerf and a leftover
/// shorter than the minimum usable offcut.
/// </summary>
public sealed record StockBar
{
    /// <summary>1-based number of the bar within its profile's plan (the order to cut them in).</summary>
    public int Number { get; init; }

    public double StockLengthMm { get; init; }

    /// <summary>The pieces in cutting order from the trimmed end (longest first). Each is the M6 line of the design
    /// member it is for (<see cref="ProfileLine.ProfileId"/>, <see cref="ProfileLine.FrameId"/>, cut angles).</summary>
    public IReadOnlyList<ProfileLine> Cuts { get; init; } = Array.Empty<ProfileLine>();

    /// <summary>Total length of the pieces.</summary>
    public double CutLengthMm { get; init; }

    /// <summary>Trim allowance removed from the start of the bar.</summary>
    public double TrimMm { get; init; }

    /// <summary>Number of saw cuts that separate pieces from the bar (each costs one kerf).</summary>
    public int SawCuts { get; init; }

    /// <summary>Material lost to those saw cuts.</summary>
    public double KerfMm { get; init; }

    /// <summary>What is left after trim, pieces and kerf: <c>stock − trim − cuts − kerf</c>.</summary>
    public double RemainingMm { get; init; }

    /// <summary>The leftover, if it is at least the minimum usable offcut (reusable); otherwise 0.</summary>
    public double RemnantMm { get; init; }

    /// <summary>Trim + kerf + a leftover too short to reuse.</summary>
    public double WasteMm { get; init; }

    /// <summary>True when the leftover is kept as a reusable remnant.</summary>
    public bool HasRemnant => RemnantMm > 0;

    /// <summary>True when there is a leftover but it is shorter than the minimum usable offcut (a waste offcut).</summary>
    public bool HasWasteOffcut => RemainingMm > 0 && !HasRemnant;

    /// <summary>Price of the whole stock bar (stock length × the profile's cost per metre); 0 for an offcut from stock.</summary>
    public decimal Cost { get; init; }

    /// <summary>The offcut in stock this bar is (Milestone 16), or null for a new stock bar.</summary>
    public long? OffcutId { get; init; }

    /// <summary>True when the pieces are cut from an offcut kept in stock rather than a new bar.</summary>
    public bool IsOffcut => OffcutId is not null;
}

/// <summary>A reusable leftover kept in stock that the cutting plan may cut pieces from (no trim: its ends are cut).</summary>
public sealed record StockOffcut(long Id, string DefinitionId, double LengthMm);

/// <summary>How many bars of one stock length to take from stock.</summary>
public sealed record StockRequirement(double StockLengthMm, int Quantity);

/// <summary>A reusable leftover returned to stock, and the bar it was cut from.</summary>
/// <param name="DefinitionId">The library profile it is a piece of.</param>
/// <param name="BarNumber">The <see cref="StockBar.Number"/> of its source bar within the profile's plan.</param>
public sealed record Remnant(string DefinitionId, string Name, double LengthMm, int BarNumber, double StockLengthMm);

/// <summary>
/// The cutting plan of one library profile. Utilisation is the share of consumed material that became pieces:
/// <c>Utilization = TotalCutMm / (TotalStockMm − TotalRemnantMm)</c>; remnants go back to stock, so they do not count
/// against it. <c>WasteFraction = TotalWasteMm / (TotalStockMm − TotalRemnantMm)</c>, so the two always add up to 1.
/// </summary>
public sealed record ProfileCuttingPlan
{
    public string DefinitionId { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>The stock lengths the library offers for this profile, shortest first.</summary>
    public IReadOnlyList<double> AvailableStockLengthsMm { get; init; } = Array.Empty<double>();

    public IReadOnlyList<StockBar> Bars { get; init; } = Array.Empty<StockBar>();

    /// <summary>New bars to take from stock, grouped by length (longest first); offcuts used are not in it.</summary>
    public IReadOnlyList<StockRequirement> Stock { get; init; } = Array.Empty<StockRequirement>();

    /// <summary>Offcuts from stock that pieces are cut from.</summary>
    public int OffcutsUsed { get; init; }

    /// <summary>Pieces that could not be planned (no stock length, or longer than every usable bar); see the issues.</summary>
    public IReadOnlyList<ProfileLine> Unplaced { get; init; } = Array.Empty<ProfileLine>();

    public int PieceCount { get; init; }
    public double TotalStockMm { get; init; }
    public double TotalCutMm { get; init; }
    public double TotalTrimMm { get; init; }
    public double TotalKerfMm { get; init; }
    public double TotalRemnantMm { get; init; }
    public int RemnantCount { get; init; }

    /// <summary>The reusable leftovers, in bar order.</summary>
    public IReadOnlyList<Remnant> Remnants { get; init; } = Array.Empty<Remnant>();

    /// <summary>Trim + kerf + leftovers too short to reuse, over every bar.</summary>
    public double TotalWasteMm { get; init; }

    /// <summary>Number of bars whose leftover is a waste offcut (see <see cref="StockBar.HasWasteOffcut"/>).</summary>
    public int WasteOffcutCount { get; init; }

    /// <summary>Fraction (0–1) of the consumed material that became pieces. See the type summary.</summary>
    public double Utilization { get; init; }

    /// <summary>Fraction (0–1) of the consumed material that is waste; <c>1 − Utilization</c>.</summary>
    public double WasteFraction { get; init; }

    public decimal CostPerMetre { get; init; }

    /// <summary>Price of all bars taken from stock.</summary>
    public decimal StockCost { get; init; }

    /// <summary>Value of the remnants returned to stock (remnant length × cost per metre).</summary>
    public decimal RemnantValue { get; init; }

    /// <summary>Cost of the material actually consumed: <c>StockCost − RemnantValue</c>.</summary>
    public decimal NetCost => StockCost - RemnantValue;

    /// <summary>Weight of all bars taken from stock, if the library gives a weight per metre.</summary>
    public double StockWeightKg { get; init; }
}

/// <summary>
/// The cutting plan of a whole calculation: one <see cref="ProfileCuttingPlan"/> per library profile, ordered by
/// profile id. Deterministic: equal pieces, library and rules always give an equal plan.
/// </summary>
public sealed record CuttingPlan
{
    public static CuttingPlan Empty { get; } = new();

    public string Currency { get; init; } = "";

    /// <summary>The saw rules the plan was made with.</summary>
    public CuttingRules Rules { get; init; } = new();

    public IReadOnlyList<ProfileCuttingPlan> Profiles { get; init; } = Array.Empty<ProfileCuttingPlan>();

    /// <summary>Pieces that could not be planned and why (unknown profile, no stock length, piece too long).</summary>
    public IReadOnlyList<CalculationIssue> Issues { get; init; } = Array.Empty<CalculationIssue>();

    public int BarCount { get; init; }
    public double TotalStockMm { get; init; }
    public double TotalCutMm { get; init; }
    public double TotalRemnantMm { get; init; }
    public double TotalWasteMm { get; init; }

    /// <summary><c>TotalCutMm / (TotalStockMm − TotalRemnantMm)</c> over every profile.</summary>
    public double Utilization { get; init; }

    public double WasteFraction { get; init; }

    public decimal StockCost { get; init; }
    public decimal RemnantValue { get; init; }
    public decimal NetCost => StockCost - RemnantValue;

    /// <summary>Every reusable leftover of the plan, by profile id then bar.</summary>
    public IEnumerable<Remnant> Remnants => Profiles.SelectMany(p => p.Remnants);

    /// <summary>The offcuts from stock the plan cuts pieces from.</summary>
    public IEnumerable<long> OffcutIdsUsed => Profiles.SelectMany(p => p.Bars).Where(b => b.OffcutId is not null).Select(b => b.OffcutId!.Value);

    /// <summary>True when every piece was placed on a bar.</summary>
    public bool IsComplete => Issues.All(i => i.Severity != IssueSeverity.Error);

    public ProfileCuttingPlan? FindProfile(string definitionId)
        => Profiles.FirstOrDefault(p => string.Equals(p.DefinitionId, definitionId, StringComparison.Ordinal));
}
