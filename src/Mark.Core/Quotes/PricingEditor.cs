using Mark.Core.Design;
using Mark.Core.Models;

namespace Mark.Core.Quotes;

/// <summary>
/// Validated changes to a quote's <see cref="PriceStructure"/>. A rejected structure leaves the project untouched;
/// an accepted one is stored as a copy (names trimmed), so later edits to the caller's object do not leak in.
/// </summary>
public static class PricingEditor
{
    public const int MaxHeads = 40;
    public const int MaxNameLength = 60;
    public const decimal MaxPercent = 1000m;
    public const decimal MaxAmount = 100_000_000m;

    public static void SetPricing(Project project, PriceStructure pricing) => TrySetPricing(project, pricing).ThrowIfFailed();

    public static EditResult TrySetPricing(Project project, PriceStructure pricing)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Validate(pricing) is { } error)
            return EditResult.Fail(error);
        project.Pricing = Clean(pricing);
        return EditResult.Ok;
    }

    /// <summary>Why <paramref name="pricing"/> cannot be used, or null if it is fine.</summary>
    public static string? Validate(PriceStructure pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        if (string.IsNullOrWhiteSpace(pricing.Name))
            return "Give the price structure a name.";
        if (pricing.Name.Trim().Length > MaxNameLength)
            return $"The price structure name can be at most {MaxNameLength} characters.";
        if (pricing.Heads.Count + pricing.Charges.Count > MaxHeads)
            return $"A price structure can have at most {MaxHeads} lines.";

        foreach (var head in pricing.Heads)
        {
            if (CheckHead(head) is { } error) return error;
            if (head.Basis == CostBasis.FixedPerQuote)
                return $"'{head.Name.Trim()}' is a fixed amount per quote: put it under charges (after the discount).";
        }
        foreach (var charge in pricing.Charges)
        {
            if (CheckHead(charge) is { } error) return error;
            if (charge.Basis is not (CostBasis.FixedPerQuote or CostBasis.PerWindow or CostBasis.PerSquareMetreOfWindow))
                return $"The charge '{charge.Name.Trim()}' must be a fixed amount per quote, per window or per m² of window.";
        }

        if (pricing.DiscountPercent is < 0 or > 100)
            return "The discount must be between 0 and 100 %.";
        if (pricing.TaxPercent is < 0 or > 100)
            return "The tax must be between 0 and 100 %.";
        if (string.IsNullOrWhiteSpace(pricing.TaxName) && pricing.TaxPercent != 0)
            return "Give the tax a name (e.g. GST).";

        var r = pricing.Rates;
        foreach (var (label, value) in new[]
                 {
                     ("Casement hardware", r.CasementHardware), ("Top/bottom hung hardware", r.HungHardware),
                     ("Tilt & turn hardware", r.TiltTurnHardware), ("Pivot hardware", r.PivotHardware),
                     ("Sliding hardware", r.SlidingHardware), ("Mesh", r.MeshPerSquareMetre),
                     ("Reinforcement", r.ReinforcementPerMetre)
                 })
        {
            if (value < 0 || value > MaxAmount)
                return $"The {label.ToLowerInvariant()} rate must be between 0 and {MaxAmount:N0}.";
        }
        return null;
    }

    private static string? CheckHead(CostHead head)
    {
        string name = (head.Name ?? "").Trim();
        if (name.Length == 0)
            return "Every cost line needs a name.";
        if (name.Length > MaxNameLength)
            return $"The cost line name '{name[..20]}…' is longer than {MaxNameLength} characters.";
        if (!Enum.IsDefined(head.Basis))
            return $"'{name}' has an unknown basis.";
        if (CostHead.IsPercent(head.Basis))
        {
            if (head.Rate is < 0 or > MaxPercent)
                return $"'{name}' must be between 0 and {MaxPercent:0} %.";
        }
        else if (head.Rate < 0 || head.Rate > MaxAmount)
            return $"'{name}' must be between 0 and {MaxAmount:N0}.";
        return null;
    }

    private static PriceStructure Clean(PriceStructure pricing)
    {
        var copy = pricing.Copy();
        copy.Name = copy.Name.Trim();
        copy.TaxName = (copy.TaxName ?? "").Trim();
        foreach (var head in copy.Heads.Concat(copy.Charges))
            head.Name = head.Name.Trim();
        return copy;
    }
}
