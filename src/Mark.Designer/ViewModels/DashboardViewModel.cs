using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>A number on the dashboard, e.g. "Won · 3 · 2,45,000.00 INR".</summary>
public sealed record DashboardTile(string Title, string Count, string Detail, string Accent);

/// <summary>A bar of the value-by-status chart; <see cref="Fraction"/> is 0–1 of the largest bar.</summary>
public sealed record DashboardBar(string Label, string Value, double Fraction, string Accent);

/// <summary>
/// The dashboard: quotes created this month, active, won and lost (count and value), the win rate, value by status and
/// the most recent quotes (click to open). Computed from the saved quotes' summaries when the page is shown.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly Func<IProjectRepository?> _projects;
    private readonly Func<DateTime> _now;

    public DashboardViewModel(Func<IProjectRepository?> projects, Func<Guid, string?> open, Action newQuote,
        Func<DateTime>? now = null)
    {
        _projects = projects;
        _now = now ?? (() => DateTime.Now);
        OpenCommand = new RelayCommand(p => { if (p is QuoteRow row) Message = open(row.Id); });
        NewQuoteCommand = new RelayCommand(newQuote);
    }

    public ObservableCollection<DashboardTile> Tiles { get; } = new();
    public ObservableCollection<DashboardBar> ValueByStatus { get; } = new();
    public ObservableCollection<QuoteRow> RecentQuotes { get; } = new();

    /// <summary>The latest changes to all quotes: who created, saved or deleted which quote, and what changed.</summary>
    public ObservableCollection<HistoryRow> RecentActivity { get; } = new();

    public bool HasRecentActivity => RecentActivity.Count > 0;

    public ICommand OpenCommand { get; }
    public ICommand NewQuoteCommand { get; }

    private string _winRateText = "";
    /// <summary>"Win rate 60 % (3 of 5 decided quotes)".</summary>
    public string WinRateText { get => _winRateText; private set => SetProperty(ref _winRateText, value); }

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

    public void Reload()
    {
        IReadOnlyList<ProjectSummary> quotes = Array.Empty<ProjectSummary>();
        Message = null;
        if (_projects() is not { } repository)
            Message = "There is no local database, so there is nothing to show yet.";
        else
        {
            try
            {
                quotes = repository.List();
            }
            catch (DataStoreException ex)
            {
                Message = ex.Message;
            }
        }

        string currency = quotes.Select(q => q.Currency).FirstOrDefault(c => c.Length > 0) ?? "";
        var now = _now();
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var thisMonth = quotes.Where(q => q.CreatedUtc.ToLocalTime() >= monthStart).ToList();

        Tiles.Clear();
        Tiles.Add(Tile("Created this month", thisMonth, currency, "#1E63C7"));
        Tiles.Add(Tile("Active quotes", quotes.Where(q => q.Status == QuoteStatus.Active).ToList(), currency, "#E07B00"));
        Tiles.Add(Tile("Won", quotes.Where(q => q.Status == QuoteStatus.Won).ToList(), currency, "#2E9E5B"));
        Tiles.Add(Tile("Lost", quotes.Where(q => q.Status == QuoteStatus.Lost).ToList(), currency, "#C62828"));

        int won = quotes.Count(q => q.Status == QuoteStatus.Won);
        int decided = won + quotes.Count(q => q.Status == QuoteStatus.Lost);
        WinRateText = decided == 0 ? "Win rate: no quotes won or lost yet"
            : $"Win rate {Math.Round(100.0 * won / decided):0} % ({won} of {decided} decided quotes)";

        ValueByStatus.Clear();
        var values = Enum.GetValues<QuoteStatus>()
            .Select(s => (Status: s, Value: quotes.Where(q => q.Status == s).Sum(q => q.Value ?? 0)))
            .ToList();
        decimal largest = Math.Max(1, values.Max(v => v.Value));
        foreach (var (status, value) in values)
            ValueByStatus.Add(new DashboardBar(status.ToString(), $"{Money(value)} {currency}".TrimEnd(),
                (double)(value / largest), status switch
                {
                    QuoteStatus.Won => "#2E9E5B",
                    QuoteStatus.Lost => "#C62828",
                    _ => "#E07B00"
                }));

        RecentActivity.Clear();
        if (_projects() is { } history)
        {
            try
            {
                foreach (var entry in history.RecentHistory(8))
                    RecentActivity.Add(HistoryRow.Of(entry));
            }
            catch (DataStoreException ex)
            {
                Message = ex.Message;
            }
        }
        OnPropertyChanged(nameof(HasRecentActivity));

        RecentQuotes.Clear();
        foreach (var q in quotes.Take(8))
            RecentQuotes.Add(new QuoteRow(q.Id, string.IsNullOrEmpty(q.QuoteNumber) ? "—" : q.QuoteNumber, q.Name, q.ClientName,
                q.Status, q.DesignCount, q.Quantity, q.AreaM2.ToString("0.##", CultureInfo.InvariantCulture) + " m²",
                q.Value is { } v ? $"{Money(v)} {q.Currency}".TrimEnd() : "—",
                q.ModifiedUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture), false, q.ModifiedBy, q.CreatedBy));
    }

    private static DashboardTile Tile(string title, IReadOnlyList<ProjectSummary> quotes, string currency, string accent)
    {
        decimal value = quotes.Sum(q => q.Value ?? 0);
        int pieces = quotes.Sum(q => q.Quantity);
        return new DashboardTile(title, quotes.Count.ToString(CultureInfo.InvariantCulture),
            $"{Money(value)} {currency} · {pieces} pcs".Trim(), accent);
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
