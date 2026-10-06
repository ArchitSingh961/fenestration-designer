using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>A saved quote as a row of the quote list.</summary>
public sealed record QuoteRow(Guid Id, string Number, string Name, string Client, QuoteStatus Status, int Designs,
    int Quantity, string Area, string Value, string Modified, bool IsOpen, string ModifiedBy = "", string CreatedBy = "", string Order = "")
{
    public override string ToString() => $"{Number} {Name}, {Status}";

    /// <summary>"Created by Ravi · last saved by Amit" for the tooltip of the Saved by column.</summary>
    public string AuthorsText => CreatedBy.Length == 0 ? "" : $"Created by {CreatedBy} · last saved by {ModifiedBy}";
}

/// <summary>Which quotes the list shows.</summary>
public enum QuoteFilter { Active, Won, Lost, All }

/// <summary>
/// The Quotes page: every saved quote (number, project, client, status, designs, pieces, area, value, last change), with
/// Active / Won / Lost / All tabs and search over number, project and client. Open, New quote and Delete. Reads the
/// database when shown (<see cref="Reload"/>); filtering and searching work on that snapshot.
/// </summary>
public sealed class QuoteListViewModel : ViewModelBase
{
    private readonly Func<IProjectRepository?> _projects;
    private readonly Func<Guid> _openProjectId;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<Guid, string?> _open;
    private IReadOnlyList<ProjectSummary> _all = Array.Empty<ProjectSummary>();

    /// <param name="open">Opens a saved quote in the editor (asking about unsaved changes); returns an error or null.</param>
    public QuoteListViewModel(Func<IProjectRepository?> projects, Func<Guid> openProjectId, Func<IDialogService?> dialogs,
        Func<Guid, string?> open, Action newQuote)
    {
        _projects = projects;
        _openProjectId = openProjectId;
        _dialogs = dialogs;
        _open = open;
        NewQuoteCommand = new RelayCommand(newQuote);
        OpenCommand = new RelayCommand(p => OpenRow(p as QuoteRow ?? SelectedQuote), p => (p as QuoteRow ?? SelectedQuote) is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedQuote is not null);
        RefreshCommand = new RelayCommand(Reload);
    }

    public ObservableCollection<QuoteRow> Quotes { get; } = new();

    public ICommand NewQuoteCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RefreshCommand { get; }

    private QuoteFilter _filter = QuoteFilter.All;                // every quote: a won one is not "gone"
    public QuoteFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                ApplyFilter();
        }
    }

    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private QuoteRow? _selectedQuote;
    public QuoteRow? SelectedQuote
    {
        get => _selectedQuote;
        set
        {
            if (!SetProperty(ref _selectedQuote, value)) return;
            ((RelayCommand)OpenCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Tab captions with counts, e.g. "Active (4)".</summary>
    public string ActiveHeader => $"Active ({_all.Count(q => q.Status == QuoteStatus.Active)})";
    public string WonHeader => $"Won ({_all.Count(q => q.Status == QuoteStatus.Won)})";
    public string LostHeader => $"Lost ({_all.Count(q => q.Status == QuoteStatus.Lost)})";
    public string AllHeader => $"All ({_all.Count})";

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            MessageIsError = true;                                   // unless the caller says it reports something done
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError = true;
    /// <summary>False for a message that reports something done (a quote deleted).</summary>
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    /// <summary>"3 quotes" (or "3 of 12 quotes" when filtered).</summary>
    public string CountText => Quotes.Count == _all.Count
        ? $"{_all.Count} quote{(_all.Count == 1 ? "" : "s")}"
        : $"{Quotes.Count} of {_all.Count} quotes";

    /// <summary>The list is empty: <see cref="EmptyText"/> says why and what to do.</summary>
    public bool IsEmpty => Quotes.Count == 0 && !HasMessage;

    public string EmptyText
    {
        get
        {
            if (_all.Count == 0) return "No quotes yet. Start one with New quote, or from an enquiry (Sales › Enquiries › Create quote).";
            string filter = Filter == QuoteFilter.All ? "" : $" {Filter.ToString().ToLowerInvariant()}";
            string search = string.IsNullOrWhiteSpace(SearchText) ? "" : $" matching \"{SearchText!.Trim()}\"";
            return $"No{filter} quotes{search}. All ({_all.Count}) shows every quote.";
        }
    }

    /// <summary>Reads the saved quotes again (when the page is shown, after a save or delete).</summary>
    public void Reload()
    {
        var repository = _projects();
        if (repository is null)
        {
            _all = Array.Empty<ProjectSummary>();
            Message = "There is no local database, so quotes cannot be listed.";
        }
        else
        {
            try
            {
                _all = repository.List();
                Message = null;
            }
            catch (DataStoreException ex)
            {
                _all = Array.Empty<ProjectSummary>();
                Message = ex.Message;
            }
        }
        OnPropertyChanged(nameof(ActiveHeader));
        OnPropertyChanged(nameof(WonHeader));
        OnPropertyChanged(nameof(LostHeader));
        OnPropertyChanged(nameof(AllHeader));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keep = SelectedQuote?.Id;
        Quotes.Clear();
        var words = (SearchText ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Guid openId = _openProjectId();
        foreach (var q in _all)
        {
            if (Filter != QuoteFilter.All && q.Status.ToString() != Filter.ToString()) continue;
            string haystack = $"{q.QuoteNumber} {q.Name} {q.ClientName} {q.CreatedBy} {q.ModifiedBy} {q.OrderNumber} {q.ClientCity}";
            if (!words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            Quotes.Add(ToRow(q, q.Id == openId));
        }
        SelectedQuote = Quotes.FirstOrDefault(r => r.Id == keep) ?? Quotes.FirstOrDefault();
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private static QuoteRow ToRow(ProjectSummary q, bool isOpen) => new(
        q.Id,
        string.IsNullOrEmpty(q.QuoteNumber) ? "—" : q.NumberText,
        q.Name,
        q.ClientName,
        q.Status,
        q.DesignCount,
        q.Quantity,
        q.AreaM2.ToString("0.##", CultureInfo.InvariantCulture) + " m²",
        q.Value is { } v ? $"{v.ToString("N2", CultureInfo.InvariantCulture)} {q.Currency}".TrimEnd() : "—",
        q.ModifiedUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture),
        isOpen,
        q.ModifiedBy,
        q.CreatedBy,
        q.OrderNumber);

    private void OpenRow(QuoteRow? row)
    {
        if (row is null) return;
        Message = _open(row.Id);
    }

    /// <summary>Why quotes cannot be deleted now (MARK is read-only), or null. Set by the main view model.</summary>
    public Func<string?>? Blocked { get; set; }

    private void Delete()
    {
        if (SelectedQuote is not { } row || _projects() is not { } repository) return;
        if (Blocked?.Invoke() is { } blocked)
        {
            Message = blocked;
            return;
        }
        if (row.Id == _openProjectId())
        {
            Message = "This quote is open. Start a new quote or open another one first to delete it.";
            return;
        }
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Delete quote", $"Delete {row.Number} '{row.Name}' permanently?"))
            return;
        try
        {
            repository.Delete(row.Id);
            Reload();
            Message = $"Deleted {row.Number} '{row.Name}'.";
            MessageIsError = false;
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
    }
}
