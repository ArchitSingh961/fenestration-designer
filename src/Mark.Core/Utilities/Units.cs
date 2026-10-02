namespace Mark.Core.Utilities;

/// <summary>
/// Constants and helpers for the unit system.
/// The application uses millimetres as the canonical unit throughout.
/// </summary>
public static class Units
{
    /// <summary>The canonical unit string used in project files.</summary>
    public const string Millimetres = "mm";

    /// <summary>Minimum valid dimension in mm (prevents zero-area geometry).</summary>
    public const double MinDimensionMm = 1.0;

    /// <summary>Maximum valid dimension in mm (sanity guard — ~30 metres).</summary>
    public const double MaxDimensionMm = 30_000.0;

    /// <summary>Default grid spacing in mm.</summary>
    public const double DefaultGridSpacingMm = 50.0;

    /// <summary>Default snap tolerance in mm.</summary>
    public const double DefaultSnapToleranceMm = 10.0;

    /// <summary>Default profile thickness in mm.</summary>
    public const double DefaultProfileThicknessMm = 50.0;

    /// <summary>Maximum plausible profile or glass thickness in mm (sanity guard).</summary>
    public const double MaxThicknessMm = 500.0;

    /// <summary>Default glass thickness in mm.</summary>
    public const double DefaultGlassThicknessMm = 6.0;
}
