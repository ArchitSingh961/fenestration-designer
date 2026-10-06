using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;

namespace Mark.Calculation;

/// <summary>One amount in a design's price, e.g. "Fabrication labour 1,012.50" (for ONE window).</summary>
public sealed record PriceComponent(string Name, decimal Amount, bool ShowOnQuote);

/// <summary>What a line of a cost sheet is.</summary>
public enum CostSheetLineKind { Material, Head, Subtotal, Price }

/// <summary>
/// A line of one window's cost sheet: the material lines, the cost lines in order, then basic value, discount and price.
/// </summary>
/// <param name="Basis">The cost line's calculation type (null for the material and price lines).</param>
/// <param name="Formula">What it is worked out from, as shown: "#PROFILECOST", "@Profile Cost", "10 % of #GLASSCOST".</param>
public sealed record CostSheetLine(string Name, CostSheetLineKind Kind, CostBasis? Basis, string Formula, decimal Amount);

/// <summary>The price of one design: per window, and for all of its windows.</summary>
public sealed record DesignPrice
{
    public Guid FrameId { get; init; }
    public int Quantity { get; init; }

    /// <summary>Library material cost of one window: profiles (incl. sash and mesh bars), glass, accessories.</summary>
    public decimal MaterialCost { get; init; }

    /// <summary>Hardware (per sash type), mesh (per m²) and reinforcement (per metre) of one window, from the rates.</summary>
    public decimal RatedCost { get; init; }

    /// <summary>The cost heads of one window, in order (subtotal lines are not in here: they add nothing).</summary>
    public IReadOnlyList<PriceComponent> Heads { get; init; } = Array.Empty<PriceComponent>();

    /// <summary>The whole cost sheet of one window, from profile cost to unit price.</summary>
    public IReadOnlyList<CostSheetLine> Sheet { get; init; } = Array.Empty<CostSheetLine>();

    /// <summary>Price of one window before discount: material + rated + heads.</summary>
    public decimal UnitBasicPrice { get; init; }

    /// <summary>Price of one window after the quote's discount (before charges and tax).</summary>
    public decimal UnitPrice { get; init; }

    /// <summary><see cref="UnitPrice"/> × <see cref="Quantity"/>.</summary>
    public decimal Total { get; init; }
}

/// <summary>A line of the quote's price summary.</summary>
public enum PriceSummaryKind { BasicValue, Discount, SubTotal, Charge, Total, Tax, GrandTotal }

public sealed record PriceSummaryLine(PriceSummaryKind Kind, string Name, decimal Amount);

/// <summary>A priced quote: each design, and the summary from basic value to grand total.</summary>
public sealed class QuotePrice
{
    public QuotePrice(string currency, IReadOnlyList<DesignPrice> designs, IReadOnlyList<PriceSummaryLine> summary)
    {
        Currency = currency;
        Designs = designs;
        Summary = summary;
    }

    public static QuotePrice Empty { get; } = new("", Array.Empty<DesignPrice>(), Array.Empty<PriceSummaryLine>());

    public string Currency { get; }
    public IReadOnlyList<DesignPrice> Designs { get; }
    public IReadOnlyList<PriceSummaryLine> Summary { get; }

    public decimal BasicValue => Amount(PriceSummaryKind.BasicValue);
    public decimal Discount => Amount(PriceSummaryKind.Discount);
    public decimal SubTotal => Amount(PriceSummaryKind.SubTotal);
    public decimal Total => Amount(PriceSummaryKind.Total);
    public decimal Tax => Amount(PriceSummaryKind.Tax);
    public decimal GrandTotal => Amount(PriceSummaryKind.GrandTotal);

    public DesignPrice? FindDesign(Guid frameId) => Designs.FirstOrDefault(d => d.FrameId == frameId);

    private decimal Amount(PriceSummaryKind kind) => Summary.Where(l => l.Kind == kind).Sum(l => l.Amount);
}

/// <summary>
/// Prices a calculated design with a <see cref="PriceStructure"/>. Pure and deterministic, no UI.
/// <code>
/// per window:  material (library) + rated (hardware per sash, mesh per m², reinforcement per m) + heads in order
///              = basic price;  basic price × (1 − discount %) = unit price
/// quote:       basic value = Σ basic price × quantity;  − discount = sub-total;  + charges = total;  + tax = grand total
/// </code>
/// Money is rounded to the calculation's decimals (away from zero) at every line, so the summary adds up exactly.
/// </summary>
public static class PricingEngine
{
    public static QuotePrice Price(Project project, CalculationResult calculation, PriceStructure pricing, int moneyDecimals = 2)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(pricing);
        decimal Money(decimal value) => Math.Round(value, moneyDecimals, MidpointRounding.AwayFromZero);

        var designs = new List<DesignPrice>();
        decimal discountFactor = 1 - pricing.DiscountPercent / 100m;
        int windows = 0;
        decimal windowArea = 0;

        foreach (var frame in project.Frames)
        {
            if (calculation.FindFrame(frame.Id) is not { } calc) continue;
            int quantity = Math.Max(1, frame.Design.Quantity);
            windows += quantity;
            windowArea += (decimal)calc.AreaM2 * quantity;

            var openings = calculation.Openings.Where(o => o.FrameId == frame.Id).ToList();
            int sashes = openings.Count(o => o.HasSash);
            // Per-sash hardware rates stand in for hardware the library does not list (no opening set for the opening).
            decimal ratedHardware = openings.Where(o => o.HasSash && !o.HasHardwareSet).Sum(o => pricing.Rates.HardwareFor(o.Opening));
            decimal mesh = Money((decimal)openings.Sum(o => o.MeshAreaM2) * pricing.MeshRateFor(frame.Design.MeshType));
            // The rate stands in for reinforcement the library does not list; listed reinforcement is already in the cost.
            decimal ratedReinforcement = calc.ReinforcementMetres > 0 ? 0
                : Money((decimal)calc.ProfileMetres * pricing.Rates.ReinforcementPerMetre);
            decimal rated = Money(ratedHardware + mesh + ratedReinforcement);
            decimal material = calc.Cost.Total;

            // The material lines: library costs split by kind, with the rates added where they belong.
            decimal libraryReinforcement = calculation.Profiles
                .Where(p => p.FrameId == frame.Id && (p.Role == ProfileType.Reinforcement || p.PartOf == "Reinforcement")).Sum(p => p.Cost);
            decimal libraryHardware = calculation.Materials
                .Where(m => m.FrameId == frame.Id && m.Category == MaterialCategory.Hardware).Sum(m => m.Cost);
            var values = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["PROFILECOST"] = calc.Cost.Profiles - libraryReinforcement,
                ["RICOST"] = libraryReinforcement + ratedReinforcement,
                ["HWCOST"] = libraryHardware + ratedHardware,
                ["GLASSCOST"] = calc.Cost.Glass,
                ["ACCCOST"] = calc.Cost.Materials - libraryHardware,
                ["MESHCOST"] = mesh,
                ["MATERIALCOST"] = material + rated,
                ["EXTRACOST"] = Math.Max(0, frame.Design.ExtraCost),
                ["AREAM2"] = (decimal)calc.AreaM2,
                ["AREASQFT"] = (decimal)calc.AreaM2 * CostFormula.SquareFeetPerSquareMetre,
                ["GLASSM2"] = (decimal)calc.GlassAreaM2,
                ["GLASSSQFT"] = (decimal)calc.GlassAreaM2 * CostFormula.SquareFeetPerSquareMetre,
                ["PROFILEM"] = (decimal)calc.ProfileMetres,
                ["PROFILEFT"] = (decimal)calc.ProfileMetres * CostFormula.FeetPerMetre,
                ["PERIMETERM"] = (decimal)(2 * (calc.WidthMm + calc.HeightMm) / 1000.0),
                ["SASHES"] = sashes,
                ["WIDTH"] = (decimal)calc.WidthMm,
                ["HEIGHT"] = (decimal)calc.HeightMm,
                ["QTY"] = quantity
            };
            var lineValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var sheet = new List<CostSheetLine>();
            foreach (var (name, variable) in CostFormula.MaterialLines)
            {
                lineValues[name] = values[variable];
                sheet.Add(new CostSheetLine(name, CostSheetLineKind.Material, null, "#" + variable, values[variable]));
            }

            decimal running = material + rated;
            var heads = new List<PriceComponent>();
            foreach (var head in pricing.Heads)
            {
                decimal Formula() => CostFormula.Evaluate(head.Formula, lineValues.Keys.ToList(),
                    v => values.GetValueOrDefault(v), h => lineValues.GetValueOrDefault(h));
                decimal amount = Money(head.Basis switch
                {
                    CostBasis.PercentOfProfiles => calc.Cost.Profiles * head.Rate / 100m,
                    CostBasis.PercentOfGlass => calc.Cost.Glass * head.Rate / 100m,
                    CostBasis.PercentOfAccessories => calc.Cost.Materials * head.Rate / 100m,
                    CostBasis.PercentOfMaterials => (material + rated) * head.Rate / 100m,
                    CostBasis.PercentOfRunningTotal => running * head.Rate / 100m,
                    CostBasis.PerMetreOfProfile => (decimal)calc.ProfileMetres * head.Rate,
                    CostBasis.PerSquareMetreOfWindow => (decimal)calc.AreaM2 * head.Rate,
                    CostBasis.PerSquareMetreOfGlass => (decimal)calc.GlassAreaM2 * head.Rate,
                    CostBasis.PerWindow => head.Rate,
                    CostBasis.PerSash => sashes * head.Rate,
                    CostBasis.Formula => Formula(),
                    CostBasis.PercentOf => Formula() * head.Rate / 100m,
                    CostBasis.Subtotal => running,
                    CostBasis.PerSquareFootOfWindow => values["AREASQFT"] * head.Rate,
                    CostBasis.PerSquareFootOfGlass => values["GLASSSQFT"] * head.Rate,
                    CostBasis.PerFootOfProfile => values["PROFILEFT"] * head.Rate,
                    CostBasis.DesignExtraCost => values["EXTRACOST"],
                    _ => 0m
                });
                string name = head.Name.Trim();
                lineValues[name] = amount;
                if (head.Basis == CostBasis.Subtotal)
                {
                    sheet.Add(new CostSheetLine(name, CostSheetLineKind.Subtotal, head.Basis, "everything above", amount));
                    continue;
                }
                sheet.Add(new CostSheetLine(name, CostSheetLineKind.Head, head.Basis, Describe(head), amount));
                heads.Add(new PriceComponent(name, amount, head.ShowOnQuote));
                running += amount;
            }

            decimal basic = Money(running);
            decimal unit = Money(basic * discountFactor);
            sheet.Add(new CostSheetLine("Basic Value", CostSheetLineKind.Subtotal, null, "everything above", basic));
            if (pricing.DiscountPercent != 0)
                sheet.Add(new CostSheetLine("Discount", CostSheetLineKind.Head, null, $"{pricing.DiscountPercent:0.##} % of @Basic Value", unit - basic));
            sheet.Add(new CostSheetLine("Unit Price", CostSheetLineKind.Price, null, "per window", unit));
            designs.Add(new DesignPrice
            {
                FrameId = frame.Id,
                Quantity = quantity,
                MaterialCost = material,
                RatedCost = rated,
                Heads = heads,
                Sheet = sheet,
                UnitBasicPrice = basic,
                UnitPrice = unit,
                Total = Money(unit * quantity)
            });
        }

        var summary = new List<PriceSummaryLine>();
        decimal basicValue = designs.Sum(d => d.UnitBasicPrice * d.Quantity);
        summary.Add(new PriceSummaryLine(PriceSummaryKind.BasicValue, "Basic value", basicValue));
        decimal discount = Money(basicValue * pricing.DiscountPercent / 100m);
        if (pricing.DiscountPercent != 0)
            summary.Add(new PriceSummaryLine(PriceSummaryKind.Discount, $"Discount {pricing.DiscountPercent:0.##} %", -discount));
        decimal subTotal = basicValue - discount;
        summary.Add(new PriceSummaryLine(PriceSummaryKind.SubTotal, "Sub-total", subTotal));

        decimal total = subTotal;
        if (designs.Count > 0)
        {
            foreach (var charge in pricing.Charges)
            {
                decimal amount = Money(charge.Basis switch
                {
                    CostBasis.FixedPerQuote => charge.Rate,
                    CostBasis.PerWindow => charge.Rate * windows,
                    CostBasis.PerSquareMetreOfWindow => charge.Rate * windowArea,
                    CostBasis.PerSquareFootOfWindow => charge.Rate * windowArea * CostFormula.SquareFeetPerSquareMetre,
                    _ => 0m
                });
                summary.Add(new PriceSummaryLine(PriceSummaryKind.Charge, charge.Name, amount));
                total += amount;
            }
        }
        summary.Add(new PriceSummaryLine(PriceSummaryKind.Total, "Total", total));
        decimal tax = Money(total * pricing.TaxPercent / 100m);
        if (pricing.TaxPercent != 0)
            summary.Add(new PriceSummaryLine(PriceSummaryKind.Tax, $"{pricing.TaxName} {pricing.TaxPercent:0.##} %", tax));
        summary.Add(new PriceSummaryLine(PriceSummaryKind.GrandTotal, "Grand total", total + tax));

        return new QuotePrice(calculation.Currency, designs, summary);
    }

    /// <summary>How a cost line is worked out, as shown on the sheet: "#PROFILECOST", "10 % of #GLASSCOST", "750 per m² of window".</summary>
    public static string Describe(CostHead head)
    {
        ArgumentNullException.ThrowIfNull(head);
        string rate = head.Rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return head.Basis switch
        {
            CostBasis.Formula => head.Formula.Trim(),
            CostBasis.PercentOf => $"{rate} % of {head.Formula.Trim()}",
            CostBasis.PercentOfProfiles => $"{rate} % of profiles",
            CostBasis.PercentOfGlass => $"{rate} % of glass",
            CostBasis.PercentOfAccessories => $"{rate} % of accessories",
            CostBasis.PercentOfMaterials => $"{rate} % of material cost",
            CostBasis.PercentOfRunningTotal => $"{rate} % of everything above",
            CostBasis.PerMetreOfProfile => $"{rate} per metre of profile",
            CostBasis.PerFootOfProfile => $"{rate} per running ft of profile",
            CostBasis.PerSquareMetreOfWindow => $"{rate} per m² of window",
            CostBasis.PerSquareFootOfWindow => $"{rate} per sq. ft. of window",
            CostBasis.PerSquareMetreOfGlass => $"{rate} per m² of glass",
            CostBasis.PerSquareFootOfGlass => $"{rate} per sq. ft. of glass",
            CostBasis.PerWindow => $"{rate} per window",
            CostBasis.PerSash => $"{rate} per sash",
            CostBasis.DesignExtraCost => "set on each design",
            CostBasis.Subtotal => "everything above",
            CostBasis.FixedPerQuote => $"{rate} for the quote",
            _ => rate
        };
    }
}
