using Mark.Core.Interfaces;

namespace Mark.Core.Snapping;

/// <summary>
/// User-configurable snapping behaviour. The tolerance is set in <b>screen pixels</b> (like every CAD
/// program) and converted to millimetres at the current zoom with <see cref="ToleranceMm"/>. That way the
/// snap "feels" the same whether you are looking at a whole façade or a 60 mm mullion.
/// </summary>
public sealed class SnapSettings
{
    /// <summary>Default order in which snap types win when several are within tolerance.</summary>
    public static IReadOnlyList<SnapType> DefaultPriority { get; } = new[]
    {
        SnapType.Intersection,
        SnapType.Endpoint,
        SnapType.Midpoint,
        SnapType.Center,
        SnapType.Edge,
        SnapType.Grid
    };

    /// <summary>Master switch for snapping to geometry (everything except the grid).</summary>
    public bool ObjectSnapEnabled { get; set; } = true;

    /// <summary>Quantise positions to the grid when no geometry snap applies.</summary>
    public bool GridEnabled { get; set; }

    /// <summary>World grid spacing (mm) used for grid snapping.</summary>
    public double GridSpacingMm { get; set; } = 10.0;

    /// <summary>Snap radius in screen pixels.</summary>
    public double TolerancePixels { get; set; } = 8.0;

    /// <summary>When nothing snaps, positions are rounded to this increment (whole millimetres).</summary>
    public double RoundingIncrementMm { get; set; } = 1.0;

    public bool EndpointEnabled { get; set; } = true;
    public bool MidpointEnabled { get; set; } = true;
    public bool IntersectionEnabled { get; set; } = true;
    public bool CenterEnabled { get; set; } = true;
    public bool EdgeEnabled { get; set; } = true;

    /// <summary>Snap types in winning order (highest first). Types not listed never win.</summary>
    public IReadOnlyList<SnapType> Priority { get; set; } = DefaultPriority;

    /// <summary>The snap radius in world millimetres at <paramref name="zoom"/> (pixels per mm).</summary>
    public double ToleranceMm(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be positive.");
        return TolerancePixels / zoom;
    }

    public bool IsEnabled(SnapType type) => type switch
    {
        SnapType.Grid => GridEnabled,
        SnapType.Endpoint => ObjectSnapEnabled && EndpointEnabled,
        SnapType.Midpoint => ObjectSnapEnabled && MidpointEnabled,
        SnapType.Intersection => ObjectSnapEnabled && IntersectionEnabled,
        SnapType.Center => ObjectSnapEnabled && CenterEnabled,
        SnapType.Edge => ObjectSnapEnabled && EdgeEnabled,
        _ => false
    };

    /// <exception cref="ArgumentOutOfRangeException">A setting is out of range.</exception>
    public void Validate()
    {
        if (!double.IsFinite(GridSpacingMm) || GridSpacingMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(GridSpacingMm), GridSpacingMm, "Grid spacing must be positive.");
        if (!double.IsFinite(TolerancePixels) || TolerancePixels < 0)
            throw new ArgumentOutOfRangeException(nameof(TolerancePixels), TolerancePixels, "Tolerance must be non-negative.");
        if (!double.IsFinite(RoundingIncrementMm) || RoundingIncrementMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(RoundingIncrementMm), RoundingIncrementMm, "Increment must be positive.");
    }
}
