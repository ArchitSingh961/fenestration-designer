using System.Collections.ObjectModel;
using System.Globalization;
using Mark.Calculation;

namespace Mark.Designer.ViewModels;

/// <summary>Identical stock bars of one profile, as shown in the panel (e.g. "2 × 6000 mm").</summary>
/// <param name="Cuts">The pieces in cutting order, e.g. "1500 · 1500 · 1200 · 1200".</param>
/// <param name="Leftover">e.g. "remnant 584 mm · waste 16 mm".</param>
public sealed record CuttingBarRow(string Stock, string Cuts, string Leftover);

/// <param name="Summary">e.g. "6000 mm × 2 · utilisation 94.2% · 1,800.00 INR".</param>
public sealed record CuttingProfileRow(string Name, string Summary, IReadOnlyList<CuttingBarRow> Bars);

/// <summary>
/// Presents a <see cref="CuttingPlan"/> produced by the calculation layer. Formatting only: the plan is optimised by
/// <see cref="CuttingOptimizer"/> (via <see cref="CalculationService.CuttingPlan"/>), never here. Identical bars are
/// listed once with a count so long plans stay readable.
/// </summary>
public sealed class CuttingPlanViewModel : ViewModelBase
{
    public ObservableCollection<CuttingProfileRow> Profiles { get; } = new();

    private string _summaryText = "";
    /// <summary>Whole-plan totals, e.g. "3 bars · 18,500 mm · utilisation 96.4% · waste 3.6%".</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    private string _costText = "";
    /// <summary>e.g. "Bars 4,410.00 INR · remnants 120.00 · net 4,290.00".</summary>
    public string CostText
    {
        get => _costText;
        private set => SetProperty(ref _costText, value);
    }

    private string _rulesText = "";
    /// <summary>The saw rules the plan used, e.g. "Kerf 3 mm · trim 5 mm · remnant ≥ 300 mm".</summary>
    public string RulesText
    {
        get => _rulesText;
        private set => SetProperty(ref _rulesText, value);
    }

    private string? _statusText;
    /// <summary>Why pieces could not be planned, or null.</summary>
    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    private CuttingPlan _plan = CuttingPlan.Empty;
    private bool _showsCosts = true;

    /// <summary>False for a login that does not see prices (e.g. a cutter): the plan is shown without costs.</summary>
    public bool ShowsCosts
    {
        get => _showsCosts;
        set
        {
            if (SetProperty(ref _showsCosts, value))
                Show(_plan);
        }
    }

    public void Show(CuttingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plan = plan;
        Profiles.Clear();
        foreach (var profile in plan.Profiles)
        {
            var bars = profile.Bars
                .GroupBy(b => new CuttingBarRow(Mm(b.StockLengthMm), string.Join(" · ", b.Cuts.Select(c => Number(c.CutLengthMm))), Leftover(b)))
                .Select(g => g.Count() == 1 ? g.Key : g.Key with { Stock = $"{g.Count()} × {g.Key.Stock}" })
                .ToList();
            string stock = string.Join(", ", profile.Stock.Select(s => $"{s.Quantity} × {Mm(s.StockLengthMm)}"));
            string summary = profile.Bars.Count == 0
                ? $"{profile.Unplaced.Count} {(profile.Unplaced.Count == 1 ? "piece" : "pieces")} not planned"
                : _showsCosts ? $"{stock} · utilisation {Percent(profile.Utilization)} · {Money(profile.StockCost)} {plan.Currency}".TrimEnd()
                : $"{stock} · utilisation {Percent(profile.Utilization)}";
            Profiles.Add(new CuttingProfileRow(profile.Name, summary, bars));
        }

        SummaryText = plan.Profiles.Count == 0 ? ""
            : $"{plan.BarCount} {(plan.BarCount == 1 ? "bar" : "bars")} · {Mm(plan.TotalStockMm)} · " +
              $"utilisation {Percent(plan.Utilization)} · waste {Percent(plan.WasteFraction)}";
        CostText = plan.Profiles.Count == 0 || !_showsCosts ? ""
            : $"Bars {Money(plan.StockCost)} {plan.Currency} · remnants {Money(plan.RemnantValue)} · net {Money(plan.NetCost)}";
        var rules = plan.Rules;
        RulesText = $"Kerf {Number(rules.KerfMm)} mm · trim {Number(rules.TrimAllowanceMm)} mm · remnant ≥ {Number(rules.MinUsableOffcutMm)} mm";
        var errors = plan.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        StatusText = errors.Count switch
        {
            0 => null,
            1 => errors[0].Message,
            _ => $"{errors.Count} pieces could not be planned. {errors[0].Message}"
        };
    }

    public void Clear() => Show(CuttingPlan.Empty);

    private static string Leftover(StockBar bar)
        => bar.HasRemnant ? $"remnant {Mm(bar.RemnantMm)} · waste {Mm(bar.WasteMm)}" : $"waste {Mm(bar.WasteMm)}";

    private static string Number(double value) => value.ToString("#,0.#", CultureInfo.InvariantCulture);

    private static string Mm(double value) => $"{Number(value)} mm";

    private static string Percent(double fraction) => fraction.ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
