using Mark.Core.Models;

namespace Mark.Core.Library;

/// <summary>
/// The keys of a quote's own item rates (<see cref="PriceStructure.ItemRates"/>): a profile's rate per metre, a glass
/// type's per m², a material's per unit. The kinds have separate ids, so the key says which it is.
/// </summary>
public static class RateKey
{
    public static string Profile(string id) => "profile:" + id;
    public static string Glass(string id) => "glass:" + id;
    public static string Material(string id) => "material:" + id;
}

/// <summary>
/// The product library as one quote sees it: the same products, with the quote's own rates (Pricing › Profile rate,
/// Glass rate, Hardware rate …) in place of the library's prices. Everything else is the library's. The calculation
/// uses it, so the bill of materials, the cost sheet, the quotation and the invoice all use the quote's rates.
/// </summary>
public sealed class RatedLibrary : IProductLibrary
{
    private readonly IProductLibrary _library;
    private readonly IReadOnlyDictionary<string, decimal> _rates;
    private readonly IReadOnlyDictionary<string, double> _barLengths;
    private readonly Dictionary<string, ProfileDefinition> _profiles = new();
    private readonly Dictionary<string, GlassDefinition> _glass = new();
    private readonly Dictionary<string, MaterialDefinition> _materials = new();

    private RatedLibrary(IProductLibrary library, IReadOnlyDictionary<string, decimal> rates, IReadOnlyDictionary<string, double> barLengths)
    {
        _library = library;
        _rates = rates;
        _barLengths = barLengths;
        Profiles = library.Profiles.Select(Rated).ToList();
        Glass = library.Glass.Select(Rated).ToList();
        Materials = library.Materials.Select(Rated).ToList();
    }

    /// <summary><paramref name="library"/> with the rates of <paramref name="pricing"/> (the library itself when it has none).</summary>
    public static IProductLibrary For(IProductLibrary library, PriceStructure? pricing)
        => For(library, pricing, null);

    /// <summary>
    /// <paramref name="library"/> as <paramref name="project"/> uses it: its rates (Pricing tab) and its bar lengths
    /// (Products tab). The library itself when the quote has neither.
    /// </summary>
    public static IProductLibrary For(IProductLibrary library, Project? project)
        => For(library, project?.Pricing, project?.Products);

    public static IProductLibrary For(IProductLibrary library, PriceStructure? pricing, ProductSettings? products)
    {
        ArgumentNullException.ThrowIfNull(library);
        var rates = pricing?.ItemRates ?? new Dictionary<string, decimal>();
        var lengths = products?.BarLengths ?? new Dictionary<string, double>();
        if (library is RatedLibrary rated) library = rated._library;
        if (rates.Count == 0 && lengths.Count == 0) return library;
        return new RatedLibrary(library, rates, lengths);
    }

    /// <summary>The library underneath (its own prices).</summary>
    public IProductLibrary Library => _library;

    private ProfileDefinition Rated(ProfileDefinition p)
    {
        if (_rates.TryGetValue(RateKey.Profile(p.Id), out var rate)) p = p with { CostPerMetre = rate };
        // The quote's bar length is the only one its cutting plan uses.
        if (_barLengths.TryGetValue(p.Id, out var length)) p = p with { StockLengthMm = length, StockLengthsMm = Array.Empty<double>() };
        return _profiles[p.Id] = p;
    }

    private GlassDefinition Rated(GlassDefinition g)
        => _glass[g.Id] = _rates.TryGetValue(RateKey.Glass(g.Id), out var rate) ? g with { CostPerSquareMetre = rate } : g;

    private MaterialDefinition Rated(MaterialDefinition m)
        => _materials[m.Id] = _rates.TryGetValue(RateKey.Material(m.Id), out var rate) ? m with { CostPerUnit = rate } : m;

    public string Currency => _library.Currency;
    public LibraryDefaults Defaults => _library.Defaults;
    public IReadOnlyList<ProfileDefinition> Profiles { get; }
    public IReadOnlyList<GlassDefinition> Glass { get; }
    public IReadOnlyList<MaterialDefinition> Materials { get; }
    public IReadOnlyList<ProductSystem> Systems => _library.Systems;
    public IReadOnlyList<Bundle> Bundles => _library.Bundles;
    public ProductSystem? FindSystem(string? id) => _library.FindSystem(id);
    public Bundle? FindBundle(string? id) => _library.FindBundle(id);
    public ProductSystem? DefaultSystem => _library.DefaultSystem;
    public ProfileDefinition? FindProfile(string? id) => id is not null && _profiles.TryGetValue(id, out var p) ? p : null;
    public GlassDefinition? FindGlass(string? id) => id is not null && _glass.TryGetValue(id, out var g) ? g : null;
    public MaterialDefinition? FindMaterial(string? id) => id is not null && _materials.TryGetValue(id, out var m) ? m : null;
    public ProfileDefinition? DefaultProfileFor(ProfileType role) => FindProfile(_library.DefaultProfileFor(role)?.Id);
    public GlassDefinition? DefaultGlass => FindGlass(_library.DefaultGlass?.Id);

    public IReadOnlyList<ProfileDefinition> SearchProfiles(LibraryQuery query)
        => _library.SearchProfiles(query).Select(p => FindProfile(p.Id)!).ToList();

    public IReadOnlyList<GlassDefinition> SearchGlass(LibraryQuery query)
        => _library.SearchGlass(query).Select(g => FindGlass(g.Id)!).ToList();

    public IReadOnlyList<MaterialDefinition> SearchMaterials(LibraryQuery query)
        => _library.SearchMaterials(query).Select(m => FindMaterial(m.Id)!).ToList();
}
