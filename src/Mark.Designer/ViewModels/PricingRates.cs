using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>The pages of the Pricing tab: the price structure, then the quote's rates by kind of item.</summary>
public enum PricingPage { Structure, ProfileRate, ReinforcementRate, HardwareRate, AccessoryRate, GlassRate, MeshRate, DesignAddOns }

/// <summary>A page in the Pricing tab's side list.</summary>
public sealed record PricingPageChoice(PricingPage Page, string Title, string Glyph);

/// <summary>
/// One item's rate on a rate page: the library's price, and the rate this quote uses (the library's unless one was
/// entered). Typing a rate gives the quote its own; "Use library rate" goes back to the library's.
/// </summary>
public sealed class ItemRateRow : ViewModelBase
{
    private readonly Func<string, string?> _get;
    private readonly Action<string, string?> _set;

    public ItemRateRow(string key, string code, string name, string unit, decimal libraryRate,
        Func<string, string?> get, Action<string, string?> set)
    {
        Key = key;
        Code = code;
        Name = name;
        Unit = unit;
        LibraryRate = libraryRate;
        _get = get;
        _set = set;
    }

    public string Key { get; }
    public string Code { get; }
    public string Name { get; }
    public string Unit { get; }
    public decimal LibraryRate { get; }

    public string LibraryRateText => PricingViewModel.FormatNumber(LibraryRate);

    /// <summary>The rate this quote uses (the library's when the quote has none of its own).</summary>
    public string RateText
    {
        get => _get(Key) ?? LibraryRateText;
        set
        {
            if (value == RateText) return;
            _set(Key, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOwnRate));
            OnPropertyChanged(nameof(SourceText));
        }
    }

    /// <summary>The quote has its own rate for the item.</summary>
    public bool IsOwnRate => _get(Key) is not null;

    public string SourceText => IsOwnRate ? "this quote" : "library";

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Goes back to the library's price.</summary>
    public void UseLibraryRate()
    {
        if (!IsOwnRate) return;
        _set(Key, null);
        OnPropertyChanged(nameof(RateText));
        OnPropertyChanged(nameof(IsOwnRate));
        OnPropertyChanged(nameof(SourceText));
    }

    public bool Matches(string[] words)
        => words.All(w => $"{Code} {Name}".Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A design's own extra cost per window (Design add-on cost heads), changed on the spot (one undo step).</summary>
public sealed class DesignAddOnRow : ViewModelBase
{
    private readonly Func<Guid, decimal, string?> _apply;
    private readonly Action<string?> _report;

    public DesignAddOnRow(Frame frame, Func<Guid, decimal, string?> apply, Action<string?> report)
    {
        FrameId = frame.Id;
        Reference = string.IsNullOrWhiteSpace(frame.Design.Reference) ? "—" : frame.Design.Reference;
        Name = frame.Design.Name;
        Size = string.Format(CultureInfo.InvariantCulture, "{0:0} × {1:0} mm", frame.Width, frame.Height);
        Quantity = Math.Max(1, frame.Design.Quantity);
        _extraCostText = frame.Design.ExtraCost == 0 ? "" : PricingViewModel.FormatNumber(frame.Design.ExtraCost);
        _apply = apply;
        _report = report;
    }

    public Guid FrameId { get; }
    public string Reference { get; }
    public string Name { get; }
    public string Size { get; }
    public int Quantity { get; }

    private string _extraCostText;
    /// <summary>Extra cost per window; set when the box is left (or Enter), which changes the design.</summary>
    public string ExtraCostText
    {
        get => _extraCostText;
        set
        {
            if (value == _extraCostText) return;
            if (!PricingViewModel.TryParseDecimal(value, out decimal amount) || amount < 0)
            {
                _report("Enter the extra cost as an amount (or leave it empty).");
                OnPropertyChanged();
                return;
            }
            _extraCostText = value;
            _report(_apply(FrameId, amount));
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// The rate pages of the Pricing tab (as in the usual estimating software): Profile, Reinforcement, Hardware, Accessory,
/// Glass and Mesh rate list the items this quote's designs use, with the library's price and the rate this quote uses;
/// Design add-ons the extra cost of each design. Rates are part of the form: Apply (or leaving the tab, or Save) puts
/// them in the quote, whose calculation then prices every item at the quote's rate.
/// </summary>
public sealed partial class PricingViewModel
{
    public static IReadOnlyList<PricingPageChoice> Pages { get; } = new[]
    {
        new PricingPageChoice(PricingPage.Structure, "Project price structure", ""),
        new PricingPageChoice(PricingPage.ProfileRate, "Profile rate", ""),
        new PricingPageChoice(PricingPage.ReinforcementRate, "Reinforcement rate", ""),
        new PricingPageChoice(PricingPage.HardwareRate, "Hardware rate", ""),
        new PricingPageChoice(PricingPage.AccessoryRate, "Accessory rate", ""),
        new PricingPageChoice(PricingPage.GlassRate, "Glass rate", ""),
        new PricingPageChoice(PricingPage.MeshRate, "Mesh rate", ""),
        new PricingPageChoice(PricingPage.DesignAddOns, "Design add-on cost heads", "")
    };

    private PricingPage _page = PricingPage.Structure;
    /// <summary>The page shown on the Pricing tab.</summary>
    public PricingPage Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageHelp));
            OnPropertyChanged(nameof(IsStructurePage));
            OnPropertyChanged(nameof(IsItemRatePage));
            OnPropertyChanged(nameof(IsMeshPage));
            OnPropertyChanged(nameof(IsDesignAddOnPage));
            ShowRows();
        }
    }

    public string PageTitle => Pages.First(p => p.Page == _page).Title;

    public string PageHelp => _page switch
    {
        PricingPage.ProfileRate => "The profiles this quote's designs use, per metre. The library's price is used unless you enter this quote's own rate.",
        PricingPage.ReinforcementRate => "The steel (RI) inside the profiles, per metre. The flat rate below is used where the library lists no reinforcement.",
        PricingPage.HardwareRate => "Hardware from the library's hardware sets, per unit. The per-sash rates below are used for openings without a hardware set.",
        PricingPage.AccessoryRate => "Gaskets, cleats, screws and other accessories, per unit.",
        PricingPage.GlassRate => "The glass of this quote's designs, per m².",
        PricingPage.MeshRate => "Insect mesh per m², by the mesh type of the designs (Design properties › Mesh type).",
        PricingPage.DesignAddOns => "An extra cost per window for one design (the Extra cost line of its cost sheet). It changes the design at once; Undo takes it back.",
        _ => ""
    };

    public bool IsStructurePage => _page == PricingPage.Structure;
    public bool IsItemRatePage => _page is PricingPage.ProfileRate or PricingPage.ReinforcementRate or PricingPage.HardwareRate
        or PricingPage.AccessoryRate or PricingPage.GlassRate;
    public bool IsMeshPage => _page == PricingPage.MeshRate;
    public bool IsDesignAddOnPage => _page == PricingPage.DesignAddOns;

    /// <summary>The library with its own prices (the rate pages show them beside the quote's).</summary>
    public Func<IProductLibrary>? LibrarySource { get; set; }

    /// <summary>Calculates the open quote with a library (the preview uses the form's rates before they are applied).</summary>
    public Func<IProductLibrary, CalculationResult>? CalculateWith { get; set; }

    /// <summary>Sets a design's extra cost per window (one undo step); returns an error or null.</summary>
    public Func<Guid, decimal, string?>? SetDesignExtraCost { get; set; }

    // The form's own rates: key → text (items) and mesh type → text. A key that is not here uses the library's price.
    private readonly Dictionary<string, string> _rateTexts = new();
    private readonly Dictionary<string, string> _meshTexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rows of the page shown (filtered by <see cref="RateSearch"/>).</summary>
    public ObservableCollection<ItemRateRow> RateRows { get; } = new();

    public ObservableCollection<DesignAddOnRow> DesignAddOns { get; } = new();

    private List<ItemRateRow> _allRows = new();

    private string? _rateSearch;
    public string? RateSearch
    {
        get => _rateSearch;
        set
        {
            if (SetProperty(ref _rateSearch, value)) ShowRows();
        }
    }

    public bool HasNoRows => RateRows.Count == 0;

    public string NoRowsText => _allRows.Count == 0
        ? _page == PricingPage.DesignAddOns ? "No designs in this quote yet." : "None of this quote's designs uses any yet."
        : "Nothing matches the search.";

    private ICommand? _useLibraryRatesCommand;
    /// <summary>The ticked rows (or every row shown, if none is ticked) go back to the library's price.</summary>
    public ICommand UseLibraryRatesCommand => _useLibraryRatesCommand ??= new RelayCommand(UseLibraryRates);

    private ICommand? _selectAllRatesCommand;
    public ICommand SelectAllRatesCommand => _selectAllRatesCommand ??= new RelayCommand(() =>
    {
        bool select = RateRows.Any(r => !r.IsSelected);
        foreach (var row in RateRows) row.IsSelected = select;
    });

    private void UseLibraryRates()
    {
        var rows = RateRows.Where(r => r.IsSelected).ToList();
        if (rows.Count == 0) rows = RateRows.ToList();
        int changed = rows.Count(r => r.IsOwnRate);
        foreach (var row in rows)
        {
            row.UseLibraryRate();
            row.IsSelected = false;
        }
        Show(changed == 0 ? "These rows already use the library's price."
            : $"{changed} rate{(changed == 1 ? "" : "s")} back to the library's price. Apply to use {(changed == 1 ? "it" : "them")} in the quote.", false);
    }

    private string? GetRate(string key)
        => IsMeshKey(key)
            ? _meshTexts.TryGetValue(MeshName(key), out var mesh) ? mesh : null
            : _rateTexts.TryGetValue(key, out var text) ? text : null;

    private void SetRate(string key, string? text)
    {
        if (IsMeshKey(key))
        {
            if (text is null) _meshTexts.Remove(MeshName(key));
            else _meshTexts[MeshName(key)] = text;
        }
        else if (text is null) _rateTexts.Remove(key);
        else _rateTexts[key] = text;
        Changed();
    }

    private const string MeshPrefix = "mesh:";
    private static bool IsMeshKey(string key) => key.StartsWith(MeshPrefix, StringComparison.Ordinal);
    private static string MeshName(string key) => IsMeshKey(key) ? key[MeshPrefix.Length..] : key;

    /// <summary>The form's rates from <paramref name="pricing"/> (on Load).</summary>
    private void LoadRates(PriceStructure pricing)
    {
        _rateTexts.Clear();
        _meshTexts.Clear();
        foreach (var (key, rate) in pricing.ItemRates) _rateTexts[key] = FormatNumber(rate);
        foreach (var (type, rate) in pricing.MeshRates) _meshTexts[type.Trim()] = FormatNumber(rate);
        BuildRows();
    }

    /// <summary>The form's rates into <paramref name="pricing"/>, or the problem.</summary>
    private string? BuildRates(PriceStructure pricing)
    {
        foreach (var (key, text) in _rateTexts)
        {
            if (!TryParseDecimal(text, out decimal rate) || rate < 0)
                return $"Enter the rate of {RowName(key)} as an amount.";
            pricing.ItemRates[key] = rate;
        }
        foreach (var (type, text) in _meshTexts)
        {
            if (!TryParseDecimal(text, out decimal rate) || rate < 0)
                return $"Enter the mesh rate of {type} as an amount per m².";
            pricing.MeshRates[type] = rate;
        }
        return null;
    }

    private string RowName(string key) => _allRows.FirstOrDefault(r => r.Key == key)?.Name ?? key[(key.IndexOf(':') + 1)..];

    /// <summary>The form's rates differ from the quote's: the preview calculates with them.</summary>
    private bool RatesDifferFromQuote(PriceStructure pricing)
    {
        var quote = _project().Pricing;
        return !SameRates(pricing.ItemRates, quote.ItemRates) || !SameRates(pricing.MeshRates, quote.MeshRates);
    }

    private static bool SameRates(IReadOnlyDictionary<string, decimal> a, IReadOnlyDictionary<string, decimal> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);

    /// <summary>The calculation for the preview: with the form's rates when they are not applied yet.</summary>
    private CalculationResult PreviewCalculation(PriceStructure pricing)
    {
        if (CalculateWith is { } calculate && LibrarySource is { } library && RatesDifferFromQuote(pricing))
            return calculate(RatedLibrary.For(library(), pricing));
        return _calculation();
    }

    /// <summary>Rebuilds the rows from what the designs use now (rates typed so far are kept).</summary>
    private void BuildRows()
    {
        var calculation = _calculation();
        var library = LibrarySource?.Invoke();
        var project = _project();
        string currency = Currency;
        var rows = new List<(PricingPage Page, ItemRateRow Row)>();

        ItemRateRow Row(string key, string id, string? code, string name, string unit, decimal libraryRate)
            => new(key, string.IsNullOrWhiteSpace(code) ? id : code!, name, unit, libraryRate, GetRate, SetRate);

        foreach (var line in calculation.Profiles.Where(p => p.IsResolved && p.DefinitionId is not null).GroupBy(p => p.DefinitionId!))
        {
            var definition = library?.FindProfile(line.Key);
            bool steel = line.Any(p => p.Role == ProfileType.Reinforcement || p.PartOf == "Reinforcement")
                         || definition?.Roles is { Count: > 0 } roles && roles.All(r => r == ProfileType.Reinforcement);
            rows.Add((steel ? PricingPage.ReinforcementRate : PricingPage.ProfileRate,
                Row(RateKey.Profile(line.Key), line.Key, definition?.Code, definition?.Name ?? line.First().Name, $"{currency} / m",
                    definition?.CostPerMetre ?? line.First().CostPerMetre)));
        }
        foreach (var line in calculation.Glass.Where(g => g.IsResolved && g.DefinitionId is not null).GroupBy(g => g.DefinitionId!))
        {
            var definition = library?.FindGlass(line.Key);
            rows.Add((PricingPage.GlassRate, Row(RateKey.Glass(line.Key), line.Key, definition?.Code, definition?.Name ?? line.First().Name,
                $"{currency} / m²", definition?.CostPerSquareMetre ?? 0)));
        }
        foreach (var line in calculation.Materials.GroupBy(m => m.MaterialId))
        {
            var first = line.First();
            var definition = library?.FindMaterial(line.Key);
            string unit = (definition?.Unit ?? first.Unit) switch
            {
                MaterialUnit.Metre => $"{currency} / m",
                MaterialUnit.SquareMetre => $"{currency} / m²",
                _ => $"{currency} / pc"
            };
            rows.Add((first.Category == MaterialCategory.Hardware ? PricingPage.HardwareRate : PricingPage.AccessoryRate,
                Row(RateKey.Material(line.Key), line.Key, definition?.Code, definition?.Name ?? first.Name, unit, definition?.CostPerUnit ?? 0)));
        }
        // Mesh: the mesh types of the designs that have mesh; without a type, the default rate.
        var meshFrames = project.Frames.Where(f => f.GlassPanels.Any(g => g.HasMesh)).ToList();
        foreach (var type in meshFrames.Select(f => f.Design.MeshType.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            rows.Add((PricingPage.MeshRate, Row(MeshPrefix + type, type, "Mesh", type, $"{currency} / m²", DefaultMeshRate())));

        _rowsByPage = rows.GroupBy(r => r.Page).ToDictionary(g => g.Key, g => g.Select(r => r.Row).ToList());

        DesignAddOns.Clear();
        foreach (var frame in project.Frames)
            DesignAddOns.Add(new DesignAddOnRow(frame, (id, amount) => SetDesignExtraCost is { } set ? set(id, amount) : "The design cannot be changed here.",
                error => { if (error is not null) Show(error, true); }));
        ShowRows();
    }

    /// <summary>The mesh rate a type without its own uses: the form's default mesh rate (Mesh rate page).</summary>
    private decimal DefaultMeshRate() => TryParseDecimal(MeshText, out var rate) ? rate : _project().Pricing.Rates.MeshPerSquareMetre;

    private Dictionary<PricingPage, List<ItemRateRow>> _rowsByPage = new();

    private void ShowRows()
    {
        _allRows = _page == PricingPage.DesignAddOns
            ? new List<ItemRateRow>()
            : _rowsByPage.TryGetValue(_page, out var rows) ? rows : new List<ItemRateRow>();
        var words = (RateSearch ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        RateRows.Clear();
        foreach (var row in _allRows.Where(r => r.Matches(words)))
            RateRows.Add(row);
        OnPropertyChanged(nameof(HasNoRows));
        OnPropertyChanged(nameof(NoRowsText));
    }
}
