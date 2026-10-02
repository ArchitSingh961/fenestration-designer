namespace Mark.Calculation;

/// <summary>How the four outer frame members meet at the corners.</summary>
public enum FrameJointType
{
    /// <summary>45° mitres: every outer member is cut to the full outer width or height.</summary>
    Mitre,

    /// <summary>Square cuts, verticals run through: horizontals fit between the verticals.</summary>
    Butt
}

/// <summary>
/// Fabrication rules for one calculation. Generic rules only: anything that depends on the product (bite, cut
/// allowance, prices) comes from the library definitions. Rounding is fixed here so results are deterministic.
/// </summary>
public sealed record CalculationRules
{
    public FrameJointType FrameJoint { get; init; } = FrameJointType.Mitre;

    /// <summary>Gap left around the glass edge inside the rebate, deducted per side from the glass size.</summary>
    public double GlassEdgeClearanceMm { get; init; }

    /// <summary>Decimal places for cut lengths and glass sizes (mm).</summary>
    public int LengthDecimals { get; init; } = 1;

    /// <summary>Decimal places for areas (m²).</summary>
    public int AreaDecimals { get; init; } = 4;

    /// <summary>Decimal places for material quantities and weights.</summary>
    public int QuantityDecimals { get; init; } = 3;

    /// <summary>Decimal places for money.</summary>
    public int MoneyDecimals { get; init; } = 2;

    /// <summary>Saw rules for the cutting plan (kerf, trim, minimum usable offcut).</summary>
    public CuttingRules Cutting { get; init; } = new();

    /// <exception cref="ArgumentOutOfRangeException">A rule is out of range.</exception>
    public void Validate()
    {
        if (Cutting is null)
            throw new ArgumentOutOfRangeException(nameof(Cutting), "Cutting rules are required.");
        Cutting.Validate();
        if (!Enum.IsDefined(FrameJoint))
            throw new ArgumentOutOfRangeException(nameof(FrameJoint), FrameJoint, "Unknown joint type.");
        if (!double.IsFinite(GlassEdgeClearanceMm) || GlassEdgeClearanceMm < 0)
            throw new ArgumentOutOfRangeException(nameof(GlassEdgeClearanceMm), GlassEdgeClearanceMm, "Must be zero or more.");
        foreach (var (value, name) in new[] { (LengthDecimals, nameof(LengthDecimals)), (AreaDecimals, nameof(AreaDecimals)),
                     (QuantityDecimals, nameof(QuantityDecimals)), (MoneyDecimals, nameof(MoneyDecimals)) })
        {
            if (value is < 0 or > 6)
                throw new ArgumentOutOfRangeException(name, value, "Must be between 0 and 6.");
        }
    }
}
