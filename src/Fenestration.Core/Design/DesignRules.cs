using Fenestration.Core.Utilities;

namespace Fenestration.Core.Design;

/// <summary>
/// Generic design defaults and limits used by the frame designer. These are NOT manufacturer rules
/// (the profile catalogue and fabrication deductions belong to the calculation engine); they only keep
/// the drawn geometry sensible. All values are in millimetres.
/// </summary>
public sealed class DesignRules
{
    /// <summary>Visible face width of the outer frame profiles.</summary>
    public double FrameThicknessMm { get; init; } = 60.0;

    /// <summary>Visible face width of new mullions (vertical divisions).</summary>
    public double MullionThicknessMm { get; init; } = 60.0;

    /// <summary>Visible face width of new transoms (horizontal divisions).</summary>
    public double TransomThicknessMm { get; init; } = 60.0;

    /// <summary>
    /// Smallest allowed glass width or height (face to face). Edits that would produce a smaller
    /// opening are rejected rather than creating slivers the fabricator cannot build.
    /// </summary>
    public double MinGlassSizeMm { get; init; } = 50.0;

    /// <summary>Thickness assigned to newly derived glass panels.</summary>
    public double DefaultGlassThicknessMm { get; init; } = Units.DefaultGlassThicknessMm;

    /// <summary>Horizontal gap between a new frame and the right-most existing frame.</summary>
    public double FrameSpacingMm { get; init; } = 500.0;

    /// <exception cref="ArgumentOutOfRangeException">A rule is out of range.</exception>
    public void Validate()
    {
        ValidationHelper.EnsureValidThickness(FrameThicknessMm, nameof(FrameThicknessMm));
        ValidationHelper.EnsureValidThickness(MullionThicknessMm, nameof(MullionThicknessMm));
        ValidationHelper.EnsureValidThickness(TransomThicknessMm, nameof(TransomThicknessMm));
        ValidationHelper.EnsureValidThickness(DefaultGlassThicknessMm, nameof(DefaultGlassThicknessMm));
        if (!double.IsFinite(MinGlassSizeMm) || MinGlassSizeMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinGlassSizeMm), MinGlassSizeMm, "Must be positive.");
        if (!double.IsFinite(FrameSpacingMm) || FrameSpacingMm < 0)
            throw new ArgumentOutOfRangeException(nameof(FrameSpacingMm), FrameSpacingMm, "Must be non-negative.");
    }
}
