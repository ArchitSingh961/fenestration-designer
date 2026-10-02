using Mark.Core.Utilities;

namespace Mark.Calculation;

/// <summary>
/// Saw-shop rules for turning required pieces into stock bars. They describe the saw and the workshop, not a
/// product, so they live here (configurable, e.g. from <c>rules.json</c>) and never in the library or in code.
/// All lengths in mm. The defaults are neutral (no kerf, no trim, every leftover is a remnant), so a plan made
/// without configuration is still exact arithmetic; set real values for a real saw.
/// </summary>
public sealed record CuttingRules
{
    /// <summary>Material removed by each saw cut (blade width). Every piece is separated from the rest of the bar by one
    /// cut, except a piece that ends exactly at the end of the usable length.</summary>
    public double KerfMm { get; init; }

    /// <summary>Removed from the start of every stock bar to square the factory end (including that cut's kerf).</summary>
    public double TrimAllowanceMm { get; init; }

    /// <summary>A leftover at least this long is a reusable remnant; a shorter one is waste. 0 = keep every leftover.</summary>
    public double MinUsableOffcutMm { get; init; }

    /// <exception cref="ArgumentOutOfRangeException">A rule is negative, not a number or implausibly large.</exception>
    public void Validate()
    {
        foreach (var (value, name) in new[] { (KerfMm, nameof(KerfMm)), (TrimAllowanceMm, nameof(TrimAllowanceMm)),
                     (MinUsableOffcutMm, nameof(MinUsableOffcutMm)) })
        {
            if (!double.IsFinite(value) || value < 0 || value > Units.MaxDimensionMm)
                throw new ArgumentOutOfRangeException(name, value, $"Must be between 0 and {Units.MaxDimensionMm} mm.");
        }
    }
}
