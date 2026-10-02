using Mark.Core.Models;

namespace Mark.Calculation;

/// <summary>One amount in a design's price, e.g. "Fabrication labour 1,012.50" (for ONE window).</summary>
public sealed record PriceComponent(string Name, decimal Amount, bool ShowOnQuote);

/// <summary>The price of one design: per window, and for all of its windows.</summary>
public sealed record DesignPrice
{
    public Guid FrameId { get; init; }
    public int Quantity { get; init; }

    /// <summary>Library material cost of one window: profiles (incl. sash and mesh bars), glass, accessories.</summary>
    public decimal MaterialCost { get; init; }

    /// <summary>Hardware (per sash type), mesh (per m²) and reinforcement (per metre) of one window, from the rates.</summary>
    public decimal RatedCost { get; init; }

    /// <summary>The cost heads of one window, in order.</summary>
    public IReadOnlyList<PriceComponent> Heads { get; init; } = Array.Empty<PriceComponent>();

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
            decimal hardware = openings.Where(o => o.HasSash).Sum(o => pricing.Rates.HardwareFor(o.Opening));
            decimal mesh = Money((decimal)openings.Sum(o => o.MeshAreaM2) * pricing.Rates.MeshPerSquareMetre);
            decimal reinforcement = Money((decimal)calc.ProfileMetres * pricing.Rates.ReinforcementPerMetre);
            decimal rated = Money(hardware + mesh + reinforcement);
            decimal material = calc.Cost.Total;

            decimal running = material + rated;
            var heads = new List<PriceComponent>();
            foreach (var head in pricing.Heads)
            {
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
                    _ => 0m
                });
                heads.Add(new PriceComponent(head.Name, amount, head.ShowOnQuote));
                running += amount;
            }

            decimal basic = Money(running);
            decimal unit = Money(basic * discountFactor);
            designs.Add(new DesignPrice
            {
                FrameId = frame.Id,
                Quantity = quantity,
                MaterialCost = material,
                RatedCost = rated,
                Heads = heads,
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
}
