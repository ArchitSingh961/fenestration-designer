using System.Collections.ObjectModel;
using System.Globalization;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>A line of history: "3 Oct 2026, 14:20 · Amit Kumar · Saved · Status Active → Won".</summary>
public sealed record HistoryRow(string When, string Who, string What, string Detail, string Quote, Guid ProjectId)
{
    public bool HasDetail => Detail.Length > 0;

    public static HistoryRow Of(ProjectHistoryEntry e) => new(
        e.TimeUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture),
        e.Who,
        e.Action switch
        {
            ProjectAction.Created => "Created",
            ProjectAction.Deleted => "Deleted",
            _ => "Saved"
        },
        e.Detail,
        e.QuoteNumber.Length > 0 ? $"{e.QuoteNumber} · {e.ProjectName}" : e.ProjectName,
        e.ProjectId);
}

/// <summary>
/// Milestone 14, who did what: every save records the signed-in person with the quote (created by, last saved by) and
/// in the quote's history, shown on the Client tab; the dashboard shows the latest changes of all quotes.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The open quote's history, newest first (empty for a quote that was never saved).</summary>
    public ObservableCollection<HistoryRow> QuoteHistory { get; } = new();

    public bool HasQuoteHistory => QuoteHistory.Count > 0;

    /// <summary>"Created by Ravi Shah on 1 Oct 2026 · last saved by Amit Kumar on 3 Oct 2026", or "Not saved yet".</summary>
    public string QuoteAuthorsText { get; private set; } = "Not saved yet";

    private void RefreshHistory()
    {
        QuoteHistory.Clear();
        QuoteAuthorsText = "Not saved yet";
        if (Store is not null)
        {
            try
            {
                var entries = Store.Projects.History(Project.Id);
                foreach (var entry in entries.Take(50))
                    QuoteHistory.Add(HistoryRow.Of(entry));
                var created = entries.LastOrDefault(e => e.Action == ProjectAction.Created);
                var saved = entries.FirstOrDefault(e => e.Action != ProjectAction.Deleted);
                if (saved is not null)
                    QuoteAuthorsText = (created is null ? "" : $"Created by {created.Who} on {Day(created.TimeUtc)} · ")
                                       + $"last saved by {saved.Who} on {Day(saved.TimeUtc)}";
            }
            catch (DataStoreException ex)
            {
                QuoteAuthorsText = ex.Message;
            }
        }
        OnPropertyChanged(nameof(HasQuoteHistory));
        OnPropertyChanged(nameof(QuoteAuthorsText));
    }

    private static string Day(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
