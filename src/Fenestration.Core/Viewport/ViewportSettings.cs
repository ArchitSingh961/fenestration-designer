namespace Fenestration.Core.Viewport;

/// <summary>
/// Tunable viewport behaviour. Values are UI preferences, not geometry, so they live here rather than
/// as constants scattered through the rendering and interaction code. Call <see cref="Validate"/> before use.
/// </summary>
public sealed class ViewportSettings
{
    /// <summary>Smallest allowed zoom (pixels per mm). 0.05 shows a 20 m wall on a 1000 px canvas.</summary>
    public double MinZoom { get; init; } = 0.05;

    /// <summary>Largest allowed zoom (pixels per mm). 50 shows 1 mm as 50 px, enough for profile detail.</summary>
    public double MaxZoom { get; init; } = 50.0;

    /// <summary>Zoom multiplier per mouse-wheel notch or per Zoom In/Out command.</summary>
    public double ZoomFactor { get; init; } = 1.15;

    /// <summary>Fraction of the viewport left empty around content by Fit to Screen (0 = edge to edge).</summary>
    public double FitMarginFraction { get; init; } = 0.1;

    /// <summary>After Reset View, the world origin sits this many pixels in from the top-left corner.</summary>
    public double ResetOriginMarginPixels { get; init; } = 40.0;

    /// <summary>Minor grid lines are never drawn closer together than this many pixels.</summary>
    public double MinGridPixelSpacing { get; init; } = 12.0;

    /// <summary>Safety cap on grid lines per axis per class (minor/major); beyond it that class is skipped.</summary>
    public int MaxGridLinesPerAxis { get; init; } = 500;

    /// <exception cref="ArgumentOutOfRangeException">A setting is out of range.</exception>
    public void Validate()
    {
        Require(double.IsFinite(MinZoom) && MinZoom > 0, nameof(MinZoom), MinZoom, "must be positive");
        Require(double.IsFinite(MaxZoom) && MaxZoom >= MinZoom, nameof(MaxZoom), MaxZoom, "must be ≥ MinZoom");
        Require(double.IsFinite(ZoomFactor) && ZoomFactor > 1, nameof(ZoomFactor), ZoomFactor, "must be > 1");
        Require(double.IsFinite(FitMarginFraction) && FitMarginFraction >= 0 && FitMarginFraction < 1,
            nameof(FitMarginFraction), FitMarginFraction, "must be in [0, 1)");
        Require(double.IsFinite(ResetOriginMarginPixels) && ResetOriginMarginPixels >= 0,
            nameof(ResetOriginMarginPixels), ResetOriginMarginPixels, "must be ≥ 0");
        Require(double.IsFinite(MinGridPixelSpacing) && MinGridPixelSpacing > 0,
            nameof(MinGridPixelSpacing), MinGridPixelSpacing, "must be positive");
        Require(MaxGridLinesPerAxis > 0, nameof(MaxGridLinesPerAxis), MaxGridLinesPerAxis, "must be positive");
    }

    private static void Require(bool condition, string name, object value, string rule)
    {
        if (!condition)
            throw new ArgumentOutOfRangeException(name, value, $"{name} {rule}.");
    }
}
