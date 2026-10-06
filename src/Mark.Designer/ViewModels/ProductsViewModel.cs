using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Library;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>The pages of the Products tab: what the quote's designs use, by kind.</summary>
public enum ProductsPage { Profiles, Reinforcement, Hardware, Accessories, Glass }

/// <summary>A page in the Products tab's side list.</summary>
public sealed record ProductsPageChoice(ProductsPage Page, string Title, string Glyph);

/// <summary>
/// One product the quote uses: how much, and for profiles the bar length it is bought and cut in (the library's, or
/// this quote's own, changed on the spot).
/// </summary>
public sealed class ProductRow : ViewModelBase
{
    private readonly Func<ProductRow, string, string?> _setLength;

    public ProductRow(string id, string code, string name, string used, string bars, double? libraryLength, double? ownLength,
        Func<ProductRow, string, string?> setLength)
    {
        Id = id;
        Code = code;
        Name = name;
        UsedText = used;
        BarsText = bars;
        LibraryLength = libraryLength;
        OwnLength = ownLength;
        _lengthText = Mm(ownLength ?? libraryLength);
        _setLength = setLength;
    }

    public string Id { get; }
    public string Code { get; }
    public string Name { get; }

    /// <summary>"24 pcs · 36.78 m", "6 panes · 9.62 m²", "8 pcs".</summary>
    public string UsedText { get; }

    /// <summary>Profiles: the bars the cutting plan needs, "5 × 6000 mm".</summary>
    public string BarsText { get; }

    public double? LibraryLength { get; }
    public double? OwnLength { get; }

    /// <summary>The row has a bar length (a profile).</summary>
    public bool HasLength => LibraryLength is not null;

    public string LibraryLengthText => LibraryLength is { } l ? Mm(l) : "";

    public bool IsOwnLength => OwnLength is not null;

    public string SourceText => IsOwnLength ? "this quote" : "library";

    private string _lengthText;
    /// <summary>The bar length this quote uses; set when the box is left (one undo step).</summary>
    public string LengthText
    {
        get => _lengthText;
        set
        {
            if (value == _lengthText) return;
            if (_setLength(this, value) is null) _lengthText = value;
            OnPropertyChanged();
        }
    }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public bool Matches(string[] words) => words.All(w => $"{Code} {Name}".Contains(w, StringComparison.OrdinalIgnoreCase));

    public static string Mm(double? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) : "";
}

/// <summary>
/// The Products tab of the open quote (laid out like the Pricing tab): the profiles, reinforcement, hardware,
/// accessories and glass its designs use, with how much; for profiles, the bar length the quote's cutting plan, production
/// papers and stock needs use. A length can be changed for one profile, or for the ticked (or all) at once; each change
/// is one undo step and is saved with the quote.
/// </summary>
public sealed class ProductsViewModel : ViewModelBase
{
    private readonly Func<Project> _project;
    private readonly Func<CalculationResult> _calculation;
    private readonly Func<CuttingPlan?> _cuttingPlan;
    private readonly Func<IProductLibrary> _library;
    private readonly Func<ProductSettings, string, string?> _apply;
    private Dictionary<ProductsPage, List<ProductRow>> _rows = new();

    /// <param name="apply">Stores new product settings as one undo step (with its description); returns an error or null.</param>
    public ProductsViewModel(Func<Project> project, Func<CalculationResult> calculation, Func<CuttingPlan?> cuttingPlan,
        Func<IProductLibrary> library, Func<ProductSettings, string, string?> apply)
    {
        _project = project;
        _calculation = calculation;
        _cuttingPlan = cuttingPlan;
        _library = library;
        _apply = apply;
        SetLengthCommand = new RelayCommand(SetLengthForTicked);
        UseLibraryLengthsCommand = new RelayCommand(UseLibraryLengths);
        SelectAllCommand = new RelayCommand(() =>
        {
            bool select = Rows.Any(r => !r.IsSelected);
            foreach (var row in Rows) row.IsSelected = select;
        });
    }

    public static IReadOnlyList<ProductsPageChoice> Pages { get; } = new[]
    {
        new ProductsPageChoice(ProductsPage.Profiles, "Profiles", ""),
        new ProductsPageChoice(ProductsPage.Reinforcement, "Reinforcement", ""),
        new ProductsPageChoice(ProductsPage.Hardware, "Hardware", ""),
        new ProductsPageChoice(ProductsPage.Accessories, "Accessories", ""),
        new ProductsPageChoice(ProductsPage.Glass, "Glass", "")
    };

    private ProductsPage _page = ProductsPage.Profiles;
    public ProductsPage Page
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageHelp));
            OnPropertyChanged(nameof(HasLengths));
            ShowRows();
        }
    }

    public string PageTitle => Pages.First(p => p.Page == _page).Title;

    public string PageHelp => _page switch
    {
        ProductsPage.Profiles => "The profiles this quote's designs use, and the bar length they are bought and cut in. Change a length here for this quote only: the cutting plan, the production papers and the purchase needs use it.",
        ProductsPage.Reinforcement => "The steel inside the profiles, and the bar length it comes in (for this quote).",
        ProductsPage.Hardware => "Rollers, handles, locks, hinges and the other hardware the designs use.",
        ProductsPage.Accessories => "Gaskets, cleats, screws and the other accessories the designs use.",
        _ => "The glass of the designs: panes and area."
    };

    /// <summary>The page lists profiles: bar lengths can be changed.</summary>
    public bool HasLengths => _page is ProductsPage.Profiles or ProductsPage.Reinforcement;

    public ObservableCollection<ProductRow> Rows { get; } = new();

    private string? _search;
    public string? Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value)) ShowRows();
        }
    }

    private string _bulkLength = "";
    /// <summary>The length "Set length" gives the ticked rows (or all rows).</summary>
    public string BulkLength { get => _bulkLength; set => SetProperty(ref _bulkLength, value); }

    public ICommand SetLengthCommand { get; }
    public ICommand UseLibraryLengthsCommand { get; }
    public ICommand SelectAllCommand { get; }

    public bool HasNoRows => Rows.Count == 0;

    public string NoRowsText => _rows.GetValueOrDefault(_page)?.Count > 0 ? "Nothing matches the search."
        : _project().Frames.Count == 0 ? "No designs in this quote yet." : "The designs use none.";

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    private void Show(string? message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }

    // ── Rows ────────────────────────────────────────────────────────

    /// <summary>Reads what the designs use again (the tab is shown, or the quote changed).</summary>
    public void Reload()
    {
        var calculation = _calculation();
        var library = _library();
        var lengths = _project().Products.BarLengths;
        var plan = _cuttingPlan();
        var rows = new Dictionary<ProductsPage, List<ProductRow>>();
        void Add(ProductsPage page, ProductRow row)
        {
            if (!rows.TryGetValue(page, out var list)) rows[page] = list = new List<ProductRow>();
            list.Add(row);
        }

        foreach (var line in calculation.Bom.Where(b => !string.IsNullOrEmpty(b.ItemId)))
        {
            string used = Used(line);
            switch (line.Category)
            {
                case BomCategory.Profile or BomCategory.Reinforcement:
                    var profile = library.FindProfile(line.ItemId);
                    var bars = plan?.Profiles.FirstOrDefault(p => p.DefinitionId == line.ItemId);
                    string barsText = bars is null ? "" : string.Join(", ", bars.Stock.Select(s => $"{s.Quantity} × {ProductRow.Mm(s.StockLengthMm)}"));
                    Add(line.Category == BomCategory.Reinforcement ? ProductsPage.Reinforcement : ProductsPage.Profiles,
                        new ProductRow(line.ItemId, Code(profile?.Code, line.ItemId), line.Name, used, barsText,
                            profile?.StockLengthMm is > 0 ? profile.StockLengthMm : 6000,
                            lengths.TryGetValue(line.ItemId, out var own) ? own : null, SetLength));
                    break;
                case BomCategory.Glass:
                    Add(ProductsPage.Glass, new ProductRow(line.ItemId, Code(library.FindGlass(line.ItemId)?.Code, line.ItemId), line.Name, used, "", null, null, SetLength));
                    break;
                default:
                    Add(line.Category == BomCategory.Hardware ? ProductsPage.Hardware : ProductsPage.Accessories,
                        new ProductRow(line.ItemId, Code(library.FindMaterial(line.ItemId)?.Code, line.ItemId), line.Name, used, "", null, null, SetLength));
                    break;
            }
        }
        _rows = rows;
        ShowRows();
    }

    private static string Code(string? code, string id) => string.IsNullOrWhiteSpace(code) ? id : code!;

    private static string Used(BomLine line)
    {
        var parts = new List<string> { $"{line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)} {line.Unit}" };
        if (line.LengthMm is > 0 && line.Unit != "m") parts.Add($"{(line.LengthMm.Value / 1000).ToString("0.##", CultureInfo.InvariantCulture)} m");
        if (line.AreaM2 is > 0 && line.Unit != "m²") parts.Add($"{line.AreaM2.Value.ToString("0.##", CultureInfo.InvariantCulture)} m²");
        return string.Join(" · ", parts);
    }

    private void ShowRows()
    {
        var words = (Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Rows.Clear();
        foreach (var row in _rows.GetValueOrDefault(_page) ?? new List<ProductRow>())
            if (row.Matches(words)) Rows.Add(row);
        OnPropertyChanged(nameof(HasNoRows));
        OnPropertyChanged(nameof(NoRowsText));
    }

    // ── Bar lengths ─────────────────────────────────────────────────

    private string? SetLength(ProductRow row, string text)
    {
        if (!TryLength(text, out double length, out string? problem))
        {
            Show(problem, true);
            return problem;
        }
        var settings = _project().Products.Copy();
        if (row.LibraryLength is { } library && Math.Abs(library - length) < 0.05) settings.BarLengths.Remove(row.Id);
        else settings.BarLengths[row.Id] = length;
        string? error = _apply(settings, $"Bar length of {row.Name}");
        Show(error ?? $"{row.Name}: {ProductRow.Mm(length)} mm bars for this quote.", error is not null);
        return error;
    }

    private void SetLengthForTicked()
    {
        var rows = TargetRows();
        if (rows.Count == 0) return;
        if (!TryLength(BulkLength, out double length, out string? problem))
        {
            Show(problem, true);
            return;
        }
        var settings = _project().Products.Copy();
        foreach (var row in rows)
            settings.BarLengths[row.Id] = length;
        string? error = _apply(settings, $"Bar length of {rows.Count} profile{(rows.Count == 1 ? "" : "s")}");
        Show(error ?? $"{rows.Count} profile{(rows.Count == 1 ? "" : "s")} in {ProductRow.Mm(length)} mm bars for this quote.", error is not null);
    }

    private void UseLibraryLengths()
    {
        var rows = TargetRows();
        var settings = _project().Products.Copy();
        int changed = rows.Count(r => settings.BarLengths.Remove(r.Id));
        if (changed == 0)
        {
            Show("These already use the library's bar lengths.", false);
            return;
        }
        string? error = _apply(settings, "Library bar lengths");
        Show(error ?? $"{changed} profile{(changed == 1 ? "" : "s")} back on the library's bar length.", error is not null);
    }

    /// <summary>The ticked rows of the page, or every row shown when none is ticked.</summary>
    private List<ProductRow> TargetRows()
    {
        if (!HasLengths)
        {
            Show("Only profiles and reinforcement have a bar length.", true);
            return new List<ProductRow>();
        }
        var rows = Rows.Where(r => r.IsSelected).ToList();
        return rows.Count > 0 ? rows : Rows.ToList();
    }

    private static bool TryLength(string? text, out double length, out string? problem)
    {
        problem = null;
        if (!PropertiesViewModel.TryParse(text, out length) || length < ProductSettings.MinBarLengthMm || length > ProductSettings.MaxBarLengthMm)
        {
            problem = $"Enter the bar length in mm, between {ProductSettings.MinBarLengthMm:0} and {ProductSettings.MaxBarLengthMm:0}.";
            return false;
        }
        return true;
    }
}
