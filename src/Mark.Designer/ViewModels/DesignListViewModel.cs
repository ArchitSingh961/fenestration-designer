using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Designer.Rendering;

namespace Mark.Designer.ViewModels;

/// <summary>One design (window type) of the quote, as a card in the Designs tab.</summary>
public sealed class DesignCard
{
    public required Guid FrameId { get; init; }
    public required string Reference { get; init; }
    public required string Title { get; init; }
    public required string Location { get; init; }
    public required string SizeText { get; init; }
    public required string OpeningsText { get; init; }
    public required string GlassText { get; init; }
    public required int Quantity { get; init; }
    public required string UnitPriceText { get; init; }
    public required string TotalPriceText { get; init; }
    public required ImageSource Picture { get; init; }
    public required ICommand EditCommand { get; init; }
    public required ICommand DuplicateCommand { get; init; }
    public required ICommand DeleteCommand { get; init; }
}

/// <summary>
/// The Designs tab: every design of the quote as a card (picture, reference, size, openings, glass, quantity, price)
/// with Edit / Duplicate / Delete, and the quote's totals. Built from the model and the calculation; it is rebuilt when
/// the design changes, but only while the tab is shown (otherwise it is marked stale and rebuilt when shown).
/// </summary>
public sealed class DesignListViewModel : ViewModelBase
{
    private readonly Func<Project> _project;
    private readonly Func<CalculationResult> _calculation;
    private readonly Func<QuotePrice> _price;
    private readonly Func<IProductLibrary> _library;
    private readonly DesignRules _rules;
    private readonly Action<Guid> _edit;
    private readonly Func<Guid, string?> _duplicate;
    private readonly Func<Guid, string?> _delete;
    private bool _stale = true;

    public DesignListViewModel(Func<Project> project, Func<CalculationResult> calculation, Func<QuotePrice> price,
        Func<IProductLibrary> library, DesignRules rules, Action<Guid> edit, Func<Guid, string?> duplicate,
        Func<Guid, string?> delete, Action newDesign)
    {
        _project = project;
        _calculation = calculation;
        _price = price;
        _library = library;
        _rules = rules;
        _edit = edit;
        _duplicate = duplicate;
        _delete = delete;
        NewDesignCommand = new RelayCommand(newDesign);
    }

    public ObservableCollection<DesignCard> Cards { get; } = new();

    public ICommand NewDesignCommand { get; }

    private bool _isVisible;
    /// <summary>Set by the shell while the Designs tab is shown; showing it rebuilds stale cards.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && value && _stale)
                Refresh();
        }
    }

    private string _totalsText = "";
    /// <summary>"3 designs · 7 pcs · 12.6 m²".</summary>
    public string TotalsText { get => _totalsText; private set => SetProperty(ref _totalsText, value); }

    private string _valueText = "";
    /// <summary>"Designs 1,00,246.30 · Grand total 1,18,290.63 INR" (prices from the quote's price structure).</summary>
    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }

    public bool IsEmpty => Cards.Count == 0;

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    /// <summary>The design changed: rebuild now if shown, else when shown.</summary>
    public void Invalidate()
    {
        _stale = true;
        if (_isVisible) Refresh();
    }

    public void Refresh()
    {
        _stale = false;
        var project = _project();
        var result = _calculation();
        var price = _price();
        var library = _library();
        Cards.Clear();

        foreach (var frame in project.Frames)
        {
            int quantity = Math.Max(1, frame.Design.Quantity);
            decimal? unit = price.FindDesign(frame.Id)?.UnitPrice;

            var id = frame.Id;
            Cards.Add(new DesignCard
            {
                FrameId = id,
                Reference = string.IsNullOrWhiteSpace(frame.Design.Reference) ? "—" : frame.Design.Reference,
                Title = string.IsNullOrWhiteSpace(frame.Design.Name) ? "Design" : frame.Design.Name,
                Location = string.Join(" · ", new[] { frame.Design.Location, Floor(frame.Design.Floor) }.Where(s => s.Length > 0)),
                SizeText = $"{Mm(frame.Width)} × {Mm(frame.Height)} mm",
                OpeningsText = OpeningsOf(frame),
                GlassText = GlassOf(frame, library),
                Quantity = quantity,
                UnitPriceText = unit is { } p ? $"{Money(p)} each" : "Not priced",
                TotalPriceText = unit is { } q ? Money(q * quantity) : "—",
                Picture = DesignThumbnails.Render(frame, _rules, 200, 150, CardPicture, id => GlassLookIn(library, id)),
                EditCommand = new RelayCommand(() => _edit(id)),
                DuplicateCommand = new RelayCommand(() => Message = _duplicate(id)),
                DeleteCommand = new RelayCommand(() => Message = _delete(id))
            });
        }

        var totals = QuoteTotals.Of(project);
        TotalsText = totals.Designs == 0 ? "No designs yet"
            : $"{totals.Designs} design{(totals.Designs == 1 ? "" : "s")} · {totals.Quantity} pcs · {totals.AreaM2.ToString("0.##", CultureInfo.InvariantCulture)} m²";
        bool complete = result.IsComplete;
        ValueText = totals.Designs == 0 ? ""
            : $"Designs {Money(price.SubTotal)} · Grand total {Money(price.GrandTotal)} {result.Currency}".TrimEnd()
              + (complete ? "" : " (some items could not be priced: see the Drawing tab)");
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The card picture: the bare design with its opening symbols, a little bolder than a library icon.</summary>
    /// <summary>How a glass type of the library is drawn (null id: the library's default glass).</summary>
    public static Mark.Core.Library.GlassLook? GlassLookIn(Mark.Core.Library.IProductLibrary? library, string? glassId)
        => library is null ? null : (glassId is null ? library.DefaultGlass : library.FindGlass(glassId))?.Look;

    private static readonly FrameRenderOptions CardPicture = FrameRenderOptions.Thumbnail with { SymbolScale = 0.6 };

    /// <summary>"2 × Sliding, 1 × Fixed, mesh" in drawing order of first appearance.</summary>
    public static string OpeningsOf(Frame frame)
    {
        var groups = frame.GlassPanels.GroupBy(g => Simple(g.Opening)).Select(g => $"{g.Count()} × {g.Key}").ToList();
        if (frame.GlassPanels.Any(g => g.HasMesh)) groups.Add("mesh");
        return string.Join(", ", groups);

        static string Simple(OpeningType type) => type switch
        {
            OpeningType.Fixed => "Fixed",
            OpeningType.SideHungLeft or OpeningType.SideHungRight => "Casement",
            OpeningType.TopHung => "Top hung",
            OpeningType.BottomHung => "Bottom hung",
            OpeningType.TiltTurnLeft or OpeningType.TiltTurnRight => "Tilt & turn",
            OpeningType.PivotVertical or OpeningType.PivotHorizontal => "Pivot",
            _ => "Sliding"
        };
    }

    private static string GlassOf(Frame frame, IProductLibrary library)
    {
        var names = frame.GlassPanels
            .Select(g => g.GlassDefinitionId is { } id ? library.FindGlass(id)?.Name ?? id : library.DefaultGlass?.Name ?? "Glass")
            .Distinct()
            .ToList();
        return names.Count <= 2 ? string.Join(", ", names) : $"{names[0]} + {names.Count - 1} more";
    }

    private static string Floor(string floor) => string.IsNullOrWhiteSpace(floor) ? "" : $"Floor {floor}";

    private static string Mm(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
