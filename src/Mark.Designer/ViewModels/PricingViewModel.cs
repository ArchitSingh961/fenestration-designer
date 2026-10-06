using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Core.Serialization;

namespace Mark.Designer.ViewModels;

/// <summary>A choice of <see cref="CostBasis"/> with its user-facing name.</summary>
public sealed record BasisOption(CostBasis Basis, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One editable cost head (or charge) row of the Pricing tab.</summary>
public sealed class CostHeadRow : ViewModelBase
{
    private readonly Action _changed;

    public CostHeadRow(CostHead head, string currency, Action changed, Action<CostHeadRow> remove, Action<CostHeadRow, int> move)
    {
        _name = head.Name;
        _basis = head.Basis;
        _rateText = PricingViewModel.FormatNumber(head.Rate);
        _showOnQuote = head.ShowOnQuote;
        _formula = head.Formula ?? "";
        Currency = currency;
        _changed = changed;
        RemoveCommand = new RelayCommand(() => remove(this));
        MoveUpCommand = new RelayCommand(() => move(this, -1));
        MoveDownCommand = new RelayCommand(() => move(this, +1));
    }

    public string Currency { get; }

    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) _changed(); } }

    private CostBasis _basis;
    public CostBasis Basis
    {
        get => _basis;
        set
        {
            if (!SetProperty(ref _basis, value)) return;
            OnPropertyChanged(nameof(UnitText));
            OnPropertyChanged(nameof(UsesFormula));
            OnPropertyChanged(nameof(UsesRate));
            OnPropertyChanged(nameof(FormulaHint));
            _changed();
        }
    }

    private string _rateText;
    public string RateText { get => _rateText; set { if (SetProperty(ref _rateText, value)) _changed(); } }

    private bool _showOnQuote;
    public bool ShowOnQuote { get => _showOnQuote; set { if (SetProperty(ref _showOnQuote, value)) _changed(); } }

    private string _formula;
    /// <summary>The formula (custom formula) or what the percentage is of (percentage of ...).</summary>
    public string Formula { get => _formula; set { if (SetProperty(ref _formula, value)) _changed(); } }

    public bool UsesFormula => CostHead.UsesFormula(Basis);

    public bool UsesRate => CostHead.UsesRate(Basis);

    /// <summary>What to type in the formula box.</summary>
    public string FormulaHint => Basis == CostBasis.PercentOf ? "of, e.g. @Profile Cost" : "e.g. #PROFILECOST";

    public override string ToString() => Name;

    /// <summary>"%" or "INR / m²" etc., shown after the rate.</summary>
    public string UnitText => PricingViewModel.UnitOf(Basis, Currency);

    public ICommand RemoveCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    /// <summary>The row as a cost head, or an error message.</summary>
    public (CostHead? Head, string? Error) ToHead()
    {
        decimal rate = 0;
        if (UsesRate && !PricingViewModel.TryParseDecimal(RateText, out rate))
            return (null, $"Enter the rate of '{Name}' as a number.");
        return (new CostHead { Name = Name ?? "", Basis = Basis, Rate = UsesRate ? rate : 0, ShowOnQuote = ShowOnQuote,
            Formula = UsesFormula ? Formula ?? "" : "" }, null);
    }
}

/// <summary>A line of the price summary or the per-design table, formatted.</summary>
public sealed record PriceRow(string Name, string Amount, bool IsTotal);

/// <summary>A line of a design's cost sheet, formatted: name, calculation type, formula and amount per window.</summary>
public sealed record CostSheetRow(string Name, string Type, string Formula, string Amount, bool IsTotal, bool IsMaterial)
{
    public override string ToString() => $"{Name} {Amount}";
}

/// <summary>A design to show the cost sheet of.</summary>
public sealed record SheetDesign(Guid FrameId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A design's price as a row of the Pricing tab.</summary>
public sealed record DesignPriceRow(string Reference, int Quantity, string Material, string Rated, string Heads,
    string UnitPrice, string Total);

/// <summary>
/// The Pricing tab: the open quote's price structure as a form (cost heads per window, discount, charges, tax, rates for
/// hardware, mesh and reinforcement) and a live price preview of the quote. "Apply to quote" validates and stores it as
/// one undoable step. "Save as my default" makes it the starting point of every new quote; "Use my default" loads that
/// into the form. Product prices themselves are in the library (Library Manager).
/// </summary>
public sealed class PricingViewModel : ViewModelBase
{
    private readonly Func<Project> _project;
    private readonly Func<CalculationResult> _calculation;
    private readonly Func<string> _currency;
    private readonly Func<PriceStructure, string?> _apply;
    private readonly Func<PriceStructure?> _loadDefault;
    private readonly Func<PriceStructure, string?> _saveDefault;
    private string _loadedState = "";
    private bool _loading;

    public PricingViewModel(Func<Project> project, Func<CalculationResult> calculation, Func<string> currency,
        Func<PriceStructure, string?> apply, Func<PriceStructure?> loadDefault, Func<PriceStructure, string?> saveDefault,
        Action openProductPrices)
    {
        _project = project;
        _calculation = calculation;
        _currency = currency;
        _apply = apply;
        _loadDefault = loadDefault;
        _saveDefault = saveDefault;
        AddHeadCommand = new RelayCommand(() => AddRow(Heads, new CostHead { Name = NewName("New cost"), Basis = CostBasis.PerSquareFootOfWindow }));
        AddChargeCommand = new RelayCommand(() => AddRow(Charges, new CostHead { Name = "New charge", Basis = CostBasis.FixedPerQuote, ShowOnQuote = true }));
        ApplyCommand = new RelayCommand(Apply);
        RevertCommand = new RelayCommand(() => Load(_project().Pricing));
        SaveAsDefaultCommand = new RelayCommand(SaveAsDefault);
        UseDefaultCommand = new RelayCommand(UseDefault);
        OpenProductPricesCommand = new RelayCommand(openProductPrices);
    }

    public static IReadOnlyList<BasisOption> HeadBases { get; } = new[]
    {
        new BasisOption(CostBasis.Formula, "Custom formula"),
        new BasisOption(CostBasis.PercentOf, "Percentage of …"),
        new BasisOption(CostBasis.Subtotal, "Subtotal"),
        new BasisOption(CostBasis.PerSquareFootOfWindow, "per sq. ft. of window"),
        new BasisOption(CostBasis.PerSquareFootOfGlass, "per sq. ft. of glass"),
        new BasisOption(CostBasis.PerFootOfProfile, "per running ft of profile"),
        new BasisOption(CostBasis.DesignExtraCost, "Extra cost (per design)"),
        new BasisOption(CostBasis.PercentOfProfiles, "% of profiles"),
        new BasisOption(CostBasis.PercentOfGlass, "% of glass"),
        new BasisOption(CostBasis.PercentOfAccessories, "% of accessories"),
        new BasisOption(CostBasis.PercentOfMaterials, "% of material cost"),
        new BasisOption(CostBasis.PercentOfRunningTotal, "% of everything above"),
        new BasisOption(CostBasis.PerMetreOfProfile, "per metre of profile"),
        new BasisOption(CostBasis.PerSquareMetreOfWindow, "per m² of window"),
        new BasisOption(CostBasis.PerSquareMetreOfGlass, "per m² of glass"),
        new BasisOption(CostBasis.PerWindow, "per window"),
        new BasisOption(CostBasis.PerSash, "per sash")
    };

    public static IReadOnlyList<BasisOption> ChargeBases { get; } = new[]
    {
        new BasisOption(CostBasis.FixedPerQuote, "fixed per quote"),
        new BasisOption(CostBasis.PerWindow, "per window"),
        new BasisOption(CostBasis.PerSquareMetreOfWindow, "per m² of window"),
        new BasisOption(CostBasis.PerSquareFootOfWindow, "per sq. ft. of window")
    };

    /// <summary>The name of a calculation type, as in the drop-downs.</summary>
    public static string TypeName(CostBasis basis)
        => HeadBases.Concat(ChargeBases).FirstOrDefault(b => b.Basis == basis)?.Name ?? basis.ToString();

    /// <summary>The values a formula can use (#...) and the material lines (@...), for the help under the cost lines.</summary>
    public static string FormulaHelp { get; } =
        "Formulas use + − * / and brackets, the lines above (@Profile Cost, @RI Cost, @Hardware Cost, @Glass Cost, @Sub Total Including Labour …) and a window's values: "
        + string.Join(", ", Core.Quotes.CostFormula.Variables.Select(v => $"#{v.Name} ({v.Meaning})")) + ".";

    // ── Form ────────────────────────────────────────────────────────

    public ObservableCollection<CostHeadRow> Heads { get; } = new();
    public ObservableCollection<CostHeadRow> Charges { get; } = new();

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string _discountText = "0";
    public string DiscountText { get => _discountText; set => Set(ref _discountText, value); }

    private string _taxName = "";
    public string TaxName { get => _taxName; set => Set(ref _taxName, value); }

    private string _taxText = "0";
    public string TaxText { get => _taxText; set => Set(ref _taxText, value); }

    private string _casementText = "0";
    public string CasementText { get => _casementText; set => Set(ref _casementText, value); }

    private string _hungText = "0";
    public string HungText { get => _hungText; set => Set(ref _hungText, value); }

    private string _tiltTurnText = "0";
    public string TiltTurnText { get => _tiltTurnText; set => Set(ref _tiltTurnText, value); }

    private string _pivotText = "0";
    public string PivotText { get => _pivotText; set => Set(ref _pivotText, value); }

    private string _slidingText = "0";
    public string SlidingText { get => _slidingText; set => Set(ref _slidingText, value); }

    private string _meshText = "0";
    public string MeshText { get => _meshText; set => Set(ref _meshText, value); }

    private string _reinforcementText = "0";
    public string ReinforcementText { get => _reinforcementText; set => Set(ref _reinforcementText, value); }

    public string Currency => _currency();

    private bool _hasChanges;
    /// <summary>True while the form differs from the quote's price structure.</summary>
    public bool HasChanges { get => _hasChanges; private set => SetProperty(ref _hasChanges, value); }

    public ICommand AddHeadCommand { get; }
    public ICommand AddChargeCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand SaveAsDefaultCommand { get; }
    public ICommand UseDefaultCommand { get; }
    public ICommand OpenProductPricesCommand { get; }

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

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    // ── Preview ─────────────────────────────────────────────────────

    public ObservableCollection<PriceRow> Summary { get; } = new();
    public ObservableCollection<DesignPriceRow> DesignPrices { get; } = new();

    /// <summary>The designs whose cost sheet can be shown.</summary>
    public ObservableCollection<SheetDesign> SheetDesigns { get; } = new();

    /// <summary>The cost sheet of <see cref="SelectedSheetDesign"/>, one window, from profile cost to unit price.</summary>
    public ObservableCollection<CostSheetRow> Sheet { get; } = new();

    private SheetDesign? _selectedSheetDesign;
    public SheetDesign? SelectedSheetDesign
    {
        get => _selectedSheetDesign;
        set
        {
            if (SetProperty(ref _selectedSheetDesign, value))
                ShowSheet();
        }
    }

    private bool _showTypes = true;
    /// <summary>Shows the calculation type column of the cost sheet.</summary>
    public bool ShowTypes { get => _showTypes; set => SetProperty(ref _showTypes, value); }

    private QuotePrice _previewPrice = QuotePrice.Empty;

    private string _previewNote = "";
    /// <summary>Says whether the preview shows the quote as it is, or the form's unsaved changes (or why it can't).</summary>
    public string PreviewNote { get => _previewNote; private set => SetProperty(ref _previewNote, value); }

    // ── Loading and syncing ─────────────────────────────────────────

    /// <summary>Shows <paramref name="pricing"/> in the form (discarding anything typed) and refreshes the preview.</summary>
    public void Load(PriceStructure pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        _loading = true;
        Name = pricing.Name;
        DiscountText = FormatNumber(pricing.DiscountPercent);
        TaxName = pricing.TaxName;
        TaxText = FormatNumber(pricing.TaxPercent);
        var r = pricing.Rates;
        CasementText = FormatNumber(r.CasementHardware);
        HungText = FormatNumber(r.HungHardware);
        TiltTurnText = FormatNumber(r.TiltTurnHardware);
        PivotText = FormatNumber(r.PivotHardware);
        SlidingText = FormatNumber(r.SlidingHardware);
        MeshText = FormatNumber(r.MeshPerSquareMetre);
        ReinforcementText = FormatNumber(r.ReinforcementPerMetre);
        Heads.Clear();
        Charges.Clear();
        foreach (var head in pricing.Heads) Heads.Add(Row(head, Heads));
        foreach (var charge in pricing.Charges) Charges.Add(Row(charge, Charges));
        _loading = false;

        _loadedState = PricingSerializer.Serialize(_project().Pricing);
        HasChanges = PricingSerializer.Serialize(pricing) != _loadedState;
        Message = null;
        OnPropertyChanged(nameof(Currency));
        RefreshPreview();
    }

    /// <summary>
    /// The quote or the calculation changed: reload the form if the quote's pricing itself changed (undo, another quote),
    /// otherwise keep what was typed and just refresh the preview.
    /// </summary>
    public void SyncFromModel()
    {
        if (PricingSerializer.Serialize(_project().Pricing) != _loadedState)
            Load(_project().Pricing);
        else
            RefreshPreview();
    }

    /// <summary>The form as a price structure, or an error message.</summary>
    public (PriceStructure? Pricing, string? Error) TryBuild()
    {
        var pricing = new PriceStructure { Name = Name ?? "", TaxName = TaxName ?? "" };
        foreach (var (text, label, set) in new (string, string, Action<decimal>)[]
                 {
                     (DiscountText, "discount", v => pricing.DiscountPercent = v),
                     (TaxText, "tax", v => pricing.TaxPercent = v),
                     (CasementText, "casement hardware rate", v => pricing.Rates.CasementHardware = v),
                     (HungText, "top/bottom hung hardware rate", v => pricing.Rates.HungHardware = v),
                     (TiltTurnText, "tilt & turn hardware rate", v => pricing.Rates.TiltTurnHardware = v),
                     (PivotText, "pivot hardware rate", v => pricing.Rates.PivotHardware = v),
                     (SlidingText, "sliding hardware rate", v => pricing.Rates.SlidingHardware = v),
                     (MeshText, "mesh rate", v => pricing.Rates.MeshPerSquareMetre = v),
                     (ReinforcementText, "reinforcement rate", v => pricing.Rates.ReinforcementPerMetre = v)
                 })
        {
            if (!TryParseDecimal(text, out decimal value))
                return (null, $"Enter the {label} as a number.");
            set(value);
        }
        foreach (var row in Heads)
        {
            var (head, error) = row.ToHead();
            if (error is not null) return (null, error);
            pricing.Heads.Add(head!);
        }
        foreach (var row in Charges)
        {
            var (head, error) = row.ToHead();
            if (error is not null) return (null, error);
            pricing.Charges.Add(head!);
        }
        return PricingEditor.Validate(pricing) is { } invalid ? (null, invalid) : (pricing, null);
    }

    /// <summary>Recomputes the price summary and design table from the form (if valid) or the quote's pricing.</summary>
    public void RefreshPreview()
    {
        if (_loading) return;
        var (formPricing, error) = TryBuild();
        var pricing = formPricing ?? _project().Pricing;
        PreviewNote = error is not null ? $"Showing the quote's saved pricing: {error}"
            : HasChanges ? "Preview with your changes (not applied to the quote yet)."
            : "";

        var project = _project();
        var calculation = _calculation();
        var price = PricingEngine.Price(project, calculation, pricing);
        string currency = price.Currency;

        Summary.Clear();
        foreach (var line in price.Summary)
            Summary.Add(new PriceRow(line.Name, Money(line.Amount, currency),
                line.Kind is PriceSummaryKind.SubTotal or PriceSummaryKind.Total or PriceSummaryKind.GrandTotal));

        _previewPrice = price;
        var selected = SelectedSheetDesign?.FrameId;
        SheetDesigns.Clear();
        foreach (var design in price.Designs)
        {
            var f = project.Frames.First(x => x.Id == design.FrameId);
            string reference = string.IsNullOrWhiteSpace(f.Design.Reference) ? "—" : f.Design.Reference;
            SheetDesigns.Add(new SheetDesign(design.FrameId, string.IsNullOrWhiteSpace(f.Design.Name) || f.Design.Name == reference
                ? reference : $"{reference} · {f.Design.Name}"));
        }
        _selectedSheetDesign = SheetDesigns.FirstOrDefault(d => d.FrameId == selected) ?? SheetDesigns.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSheetDesign));
        ShowSheet();

        DesignPrices.Clear();
        foreach (var design in price.Designs)
        {
            var frame = project.Frames.First(f => f.Id == design.FrameId);
            DesignPrices.Add(new DesignPriceRow(
                string.IsNullOrWhiteSpace(frame.Design.Reference) ? "—" : frame.Design.Reference,
                design.Quantity,
                Money(design.MaterialCost, ""),
                Money(design.RatedCost, ""),
                Money(design.Heads.Sum(h => h.Amount), ""),
                Money(design.UnitPrice, ""),
                Money(design.Total, "")));
        }
    }

    private void ShowSheet()
    {
        Sheet.Clear();
        if (SelectedSheetDesign is null || _previewPrice.FindDesign(SelectedSheetDesign.FrameId) is not { } design) return;
        foreach (var line in design.Sheet)
        {
            string type = line.Kind switch
            {
                CostSheetLineKind.Material => "Material",
                CostSheetLineKind.Price => "",
                _ when line.Basis is { } basis => TypeName(basis),
                _ when line.Name == "Discount" => TypeName(CostBasis.PercentOf),
                _ => TypeName(CostBasis.Subtotal)
            };
            Sheet.Add(new CostSheetRow(line.Name, type, line.Formula, Money(line.Amount, ""),
                line.Kind is CostSheetLineKind.Subtotal or CostSheetLineKind.Price, line.Kind == CostSheetLineKind.Material));
        }
    }

    // ── Actions ─────────────────────────────────────────────────────

    private void Apply()
    {
        if (!HasChanges)
        {
            Show("The quote already uses this pricing.", false);
            return;
        }
        if (ApplyPending() is null)
            Show("Pricing applied to this quote.", false);
    }

    /// <summary>
    /// Applies what was typed to the quote (one undoable step), as leaving the Pricing tab and Save do, so the header,
    /// the quotation and the saved value never use an older price than the form shows. Returns the problem (also shown
    /// on the form, which keeps what was typed), or null when it was applied or nothing was typed.
    /// </summary>
    public string? ApplyPending()
    {
        if (!HasChanges) return null;
        var (pricing, error) = TryBuild();
        if (pricing is null)
        {
            Show(error!, true);
            return error;
        }
        if (_apply(pricing) is { } applyError)
        {
            Show(applyError, true);
            return applyError;
        }
        Load(_project().Pricing);
        return null;
    }

    private void SaveAsDefault()
    {
        var (pricing, error) = TryBuild();
        if (pricing is null)
        {
            Show(error!, true);
            return;
        }
        string? saveError = _saveDefault(pricing);
        Show(saveError ?? $"Saved \"{pricing.Name.Trim()}\" as your default pricing: new quotes start with it.", saveError is not null);
    }

    private void UseDefault()
    {
        if (_loadDefault() is not { } pricing)
        {
            Show("There is no local database, so there is no saved default.", true);
            return;
        }
        Load(pricing);
        Show("Loaded your default pricing into the form. Apply it to use it for this quote.", false);
    }

    private void Show(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }

    private CostHeadRow Row(CostHead head, ObservableCollection<CostHeadRow> owner)
        => new(head, Currency, Changed, row => { owner.Remove(row); Changed(); }, (row, step) =>
        {
            int i = owner.IndexOf(row), j = i + step;
            if (i < 0 || j < 0 || j >= owner.Count) return;
            owner.Move(i, j);
            Changed();
        });

    /// <summary>"New cost", or "New cost 2" ... when that name is taken (formulas need names to be unique).</summary>
    private string NewName(string name)
    {
        string candidate = name;
        for (int n = 2; Heads.Any(h => string.Equals(h.Name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    private void AddRow(ObservableCollection<CostHeadRow> owner, CostHead head)
    {
        owner.Add(Row(head, owner));
        Changed();
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
            Changed();
    }

    private void Changed()
    {
        if (_loading) return;
        HasChanges = true;
        RefreshPreview();
    }

    // ── Formatting ──────────────────────────────────────────────────

    public static string UnitOf(CostBasis basis, string currency) => basis switch
    {
        _ when CostHead.IsPercent(basis) => "%",
        CostBasis.PerMetreOfProfile => $"{currency} / m",
        CostBasis.PerSquareMetreOfWindow or CostBasis.PerSquareMetreOfGlass => $"{currency} / m²",
        CostBasis.PerSquareFootOfWindow or CostBasis.PerSquareFootOfGlass => $"{currency} / sq. ft.",
        CostBasis.PerFootOfProfile => $"{currency} / ft",
        CostBasis.Formula or CostBasis.Subtotal or CostBasis.DesignExtraCost => "",
        CostBasis.PerWindow => $"{currency} / window",
        CostBasis.PerSash => $"{currency} / sash",
        _ => currency
    };

    public static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Accepts "1,250.50", "1250.5" and the user's own culture.</summary>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        text = (text ?? "").Trim().Replace("%", "").Trim();
        if (text.Length == 0)
        {
            value = 0;
            return true;
        }
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    private static string Money(decimal value, string currency)
        => $"{value.ToString("N2", CultureInfo.InvariantCulture)} {currency}".TrimEnd();
}
