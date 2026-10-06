namespace Mark.Core.Models;

/// <summary>
/// How this quote uses the library's products, set on its Products tab: the bar length each profile is bought and cut in
/// (in place of the library's stock length). A profile without one uses the library's lengths.
/// </summary>
public sealed class ProductSettings
{
    public const double MinBarLengthMm = 500;
    public const double MaxBarLengthMm = 12_000;

    private Dictionary<string, double> _barLengths = new();

    /// <summary>Profile id → the bar length (mm) this quote's cutting plan, production papers and stock needs use.</summary>
    public Dictionary<string, double> BarLengths
    {
        get => _barLengths;
        set => _barLengths = value ?? new();
    }

    public ProductSettings Copy() => new() { BarLengths = new Dictionary<string, double>(BarLengths) };

    /// <summary>Why the settings cannot be used, or null.</summary>
    public string? Problem()
    {
        foreach (var (id, length) in BarLengths)
        {
            if (string.IsNullOrWhiteSpace(id)) return "A bar length has no profile.";
            if (!double.IsFinite(length) || length < MinBarLengthMm || length > MaxBarLengthMm)
                return $"A bar length must be between {MinBarLengthMm:0} and {MaxBarLengthMm:0} mm.";
        }
        return null;
    }
}
