using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>By week (the last 12) or by month (the last 12).</summary>
public enum ChartPeriod { Weeks, Months }

/// <summary>One bar of the counts chart, with its height in pixels.</summary>
public sealed record ChartBar(string Series, int Count, double Height, string Color, string ToolTip);

/// <summary>One period of the charts: the counts bars and the won value bar.</summary>
public sealed record ChartColumn(string Label, string LongLabel, IReadOnlyList<ChartBar> Bars, decimal WonValue, double ValueHeight,
    string ValueToolTip)
{
    /// <summary>The counts as table cells, in the order of the legend.</summary>
    public IReadOnlyList<int> Counts => Bars.Select(b => b.Count).ToList();

    public string WonValueText => WonValue == 0 ? "—" : WonValue.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
}

public sealed record ChartLegendItem(string Name, string Color);

/// <summary>A salesperson: quotes they created, won and lost, the value won, the win rate.</summary>
public sealed record SalesPersonRow(string Name, int Quotes, int Won, int Lost, string WonValue, string WinRate);

/// <summary>A city: quotes, won, value won.</summary>
public sealed record SalesCityRow(string City, int Quotes, int Won, string WonValue);

/// <summary>
/// The sales charts on the dashboard (Milestone 15): enquiries, quotes, won and lost per week or month, the value won
/// per period, and sales by person (who created the quote) and by city (the client's). Read from the database when the
/// dashboard is shown.
/// </summary>
public sealed class SalesChartsViewModel : ViewModelBase
{
    /// <summary>Bar heights at most this many pixels.</summary>
    public const double ChartHeight = 120;

    // The default validated categorical order (blue, orange, aqua, yellow); the counts table gives the exact numbers.
    private static readonly ChartLegendItem[] Series =
    {
        new("Enquiries", "#2A78D6"), new("Quotes", "#EB6834"), new("Won", "#1BAF7A"), new("Lost", "#EDA100")
    };

    private readonly Func<IProjectRepository?> _projects;
    private readonly Func<SqliteEnquiryRepository?> _enquiries;
    private readonly Func<DateTime> _now;

    public SalesChartsViewModel(Func<IProjectRepository?> projects, Func<SqliteEnquiryRepository?> enquiries, Func<DateTime>? now = null)
    {
        _projects = projects;
        _enquiries = enquiries;
        _now = now ?? (() => DateTime.Now);
        PeriodCommand = new RelayCommand(p => { if (p is ChartPeriod period) Period = period; else if (p is string s && Enum.TryParse(s, out ChartPeriod parsed)) Period = parsed; });
    }

    public ICommand PeriodCommand { get; }

    public IReadOnlyList<ChartLegendItem> Legend => Series;

    public ObservableCollection<ChartColumn> Columns { get; } = new();
    public ObservableCollection<SalesPersonRow> People { get; } = new();
    public ObservableCollection<SalesCityRow> Cities { get; } = new();

    private ChartPeriod _period = ChartPeriod.Months;
    public ChartPeriod Period
    {
        get => _period;
        set
        {
            if (!SetProperty(ref _period, value)) return;
            OnPropertyChanged(nameof(IsWeeks));
            OnPropertyChanged(nameof(IsMonths));
            Reload();
        }
    }

    public bool IsWeeks { get => _period == ChartPeriod.Weeks; set { if (value) Period = ChartPeriod.Weeks; } }
    public bool IsMonths { get => _period == ChartPeriod.Months; set { if (value) Period = ChartPeriod.Months; } }

    /// <summary>"Last 12 months: 34 enquiries, 21 quotes, 9 won (12,40,000), 4 lost".</summary>
    public string SummaryText { get; private set; } = "";

    public bool HasData { get; private set; }

    public void Reload()
    {
        IReadOnlyList<ProjectSummary> quotes = Array.Empty<ProjectSummary>();
        IReadOnlyList<EnquirySummary> enquiries = Array.Empty<EnquirySummary>();
        try
        {
            quotes = _projects()?.List() ?? quotes;
            enquiries = _enquiries()?.List() ?? enquiries;
        }
        catch (DataStoreException)
        {
            // The dashboard shows its own message for a database that cannot be read.
        }

        var periods = Periods(_now(), _period);
        var columns = periods.Select(p =>
        {
            int Count(IEnumerable<DateTime> times) => times.Count(t => t >= p.Start && t < p.End);
            var decided = quotes.Where(q => q.Status != QuoteStatus.Active).Select(q => (q.Status, When: (q.DecidedUtc ?? q.ModifiedUtc).ToLocalTime(), q.Value));
            int[] counts =
            {
                Count(enquiries.Select(e => e.CreatedUtc.ToLocalTime())),
                Count(quotes.Select(q => q.CreatedUtc.ToLocalTime())),
                Count(decided.Where(d => d.Status == QuoteStatus.Won).Select(d => d.When)),
                Count(decided.Where(d => d.Status == QuoteStatus.Lost).Select(d => d.When))
            };
            decimal won = decided.Where(d => d.Status == QuoteStatus.Won && d.When >= p.Start && d.When < p.End).Sum(d => d.Value ?? 0);
            return (p, counts, won);
        }).ToList();

        int maxCount = Math.Max(1, columns.Max(c => c.counts.Max()));
        decimal maxValue = Math.Max(1, columns.Max(c => c.won));
        Columns.Clear();
        foreach (var (p, counts, won) in columns)
        {
            var bars = Series.Select((s, i) => new ChartBar(s.Name, counts[i], counts[i] == 0 ? 0 : Math.Max(3, ChartHeight * counts[i] / maxCount),
                s.Color, $"{s.Name}: {counts[i]} in {p.LongLabel}")).ToList();
            Columns.Add(new ChartColumn(p.Label, p.LongLabel, bars, won, won == 0 ? 0 : Math.Max(3, (double)(won / maxValue) * ChartHeight),
                $"Won value in {p.LongLabel}: {won.ToString("N2", CultureInfo.GetCultureInfo("en-IN"))}"));
        }

        var start = periods[0].Start;
        var inRange = quotes.Where(q => q.CreatedUtc.ToLocalTime() >= start).ToList();
        People.Clear();
        foreach (var g in inRange.GroupBy(q => q.CreatedBy.Length > 0 ? q.CreatedBy : "Not recorded").OrderByDescending(g => g.Count()))
            People.Add(new SalesPersonRow(g.Key, g.Count(), g.Count(q => q.Status == QuoteStatus.Won), g.Count(q => q.Status == QuoteStatus.Lost),
                Money(g.Where(q => q.Status == QuoteStatus.Won).Sum(q => q.Value ?? 0)), WinRate(g)));
        Cities.Clear();
        foreach (var g in inRange.GroupBy(q => q.ClientCity.Length > 0 ? q.ClientCity : "No city").OrderByDescending(g => g.Count()).Take(10))
            Cities.Add(new SalesCityRow(g.Key, g.Count(), g.Count(q => q.Status == QuoteStatus.Won),
                Money(g.Where(q => q.Status == QuoteStatus.Won).Sum(q => q.Value ?? 0))));

        int[] totals = Enumerable.Range(0, Series.Length).Select(i => columns.Sum(c => c.counts[i])).ToArray();
        HasData = totals.Any(t => t > 0);
        SummaryText = $"Last 12 {(_period == ChartPeriod.Weeks ? "weeks" : "months")}: {totals[0]} enquiries, {totals[1]} quotes, " +
                      $"{totals[2]} won ({Money(columns.Sum(c => c.won))}), {totals[3]} lost";
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasData));
    }

    private static string WinRate(IEnumerable<ProjectSummary> quotes)
    {
        int won = quotes.Count(q => q.Status == QuoteStatus.Won), decided = won + quotes.Count(q => q.Status == QuoteStatus.Lost);
        return decided == 0 ? "—" : $"{Math.Round(100.0 * won / decided):0} %";
    }

    private static string Money(decimal value) => value.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));

    /// <summary>The last 12 weeks (Monday to Sunday) or months, oldest first, in local time.</summary>
    public static IReadOnlyList<(DateTime Start, DateTime End, string Label, string LongLabel)> Periods(DateTime now, ChartPeriod period)
    {
        var list = new List<(DateTime, DateTime, string, string)>();
        if (period == ChartPeriod.Months)
        {
            var first = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
            for (int i = 0; i < 12; i++)
            {
                var start = first.AddMonths(i);
                list.Add((start, start.AddMonths(1), start.ToString("MMM", CultureInfo.InvariantCulture),
                    start.ToString("MMM yyyy", CultureInfo.InvariantCulture)));
            }
        }
        else
        {
            var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)).AddDays(-7 * 11);
            for (int i = 0; i < 12; i++)
            {
                var start = monday.AddDays(7 * i);
                list.Add((start, start.AddDays(7), start.ToString("d MMM", CultureInfo.InvariantCulture),
                    $"the week of {start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"));
            }
        }
        return list;
    }
}
