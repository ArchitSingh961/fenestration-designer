using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>An enquiry in the list: "EN-00012 · Mr. Archit Singh · Jaipur · Site visit · Referral · Ravi · 1,20,000".</summary>
/// <param name="QuoteOnly">A quote that did not come from an enquiry (the list shows every quote too).</param>
/// <param name="QuoteValue">The value of the row's quote, when it has one (shown instead of the expected value).</param>
public sealed record EnquiryRow(EnquirySummary Summary, DateTime Today, bool QuoteOnly = false, decimal? QuoteValue = null)
{
    public override string ToString() => $"{Summary.Number} {Summary.ClientName}";

    public Guid Id => Summary.Id;
    public string Number => Summary.Number;
    public string Client => Summary.ClientName;
    public string City => Summary.City;
    public string Phone => Summary.Phone;
    public string Stage => Enquiry.StageName(Summary.Stage);
    public string Source => Summary.Source;
    public string Owner => Summary.Owner;
    public string Value => (QuoteValue ?? Summary.ExpectedValue) is { } v ? v.ToString("N0", CultureInfo.GetCultureInfo("en-IN")) : "";

    /// <summary>"Enquiry" or "Quote".</summary>
    public string Kind => QuoteOnly ? "Quote" : "Enquiry";
    public string FollowUp => Summary.FollowUp?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
    public string Quote => Summary.QuoteId is null ? "" : Summary.QuoteNumber.Length > 0 ? Summary.QuoteNumber : "Not saved yet";
    public string Created => Summary.CreatedUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public bool HasQuote => Summary.QuoteId is not null;

    /// <summary>A follow-up due today or earlier on an enquiry that is still open.</summary>
    public bool IsDue => Summary.FollowUp is { } day && day.Date <= Today.Date && Summary.Stage is not (EnquiryStage.Won or EnquiryStage.Lost);
}

/// <summary>Which enquiries the list shows.</summary>
public enum EnquiryFilter { Open, Quoted, Won, Lost, All }

/// <summary>
/// The enquiry form, in two steps: 1 — the client and the site; 2 — the enquiry (stage, source, salesperson, expected
/// value, dates, requirements).
/// </summary>
public sealed class EnquiryEditorViewModel : ViewModelBase
{
    public EnquiryEditorViewModel(Enquiry enquiry)
    {
        Original = enquiry ?? throw new ArgumentNullException(nameof(enquiry));
        var c = enquiry.Client;
        _title = c.Title; _firstName = c.FirstName; _lastName = c.LastName; _company = c.Company; _phone = c.Phone; _email = c.Email;
        _address1 = c.AddressLine1; _address2 = c.AddressLine2; _city = c.City; _state = c.State; _postalCode = c.PostalCode;
        _stage = enquiry.Stage; _source = enquiry.Source; _owner = enquiry.Owner;
        _expectedValue = enquiry.ExpectedValue?.ToString(CultureInfo.InvariantCulture) ?? "";
        _expectedClose = enquiry.ExpectedCloseDate; _followUp = enquiry.FollowUpDate;
        _requirements = enquiry.Requirements; _lostReason = enquiry.LostReason;
    }

    public Enquiry Original { get; }

    public bool IsNew => string.IsNullOrEmpty(Original.Number);

    public string Title => IsNew ? "New enquiry" : $"Enquiry {Original.Number}";

    private int _step = 1;
    /// <summary>1: client and site; 2: the enquiry.</summary>
    public int Step
    {
        get => _step;
        set
        {
            if (!SetProperty(ref _step, Math.Clamp(value, 1, 2))) return;
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(StepText));
            OnPropertyChanged(nameof(CanCreateQuote));
        }
    }

    public bool IsStep1 => _step == 1;
    public bool IsStep2 => _step == 2;

    /// <summary>A quote was made from it ("Open its quote").</summary>
    public bool HasQuote => Original.QuoteId is not null;

    /// <summary>On step 2 of an enquiry without a quote yet ("Create quote").</summary>
    public bool CanCreateQuote => IsStep2 && !HasQuote;
    public string StepText => _step == 1 ? "Step 1 of 2 · Client and site" : "Step 2 of 2 · Enquiry";

    private string _title, _firstName, _lastName, _company, _phone, _email, _address1, _address2, _city, _state, _postalCode;
    public string ClientTitle { get => _title; set => SetProperty(ref _title, value); }
    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public string Company { get => _company; set => SetProperty(ref _company, value); }
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string AddressLine1 { get => _address1; set => SetProperty(ref _address1, value); }
    public string AddressLine2 { get => _address2; set => SetProperty(ref _address2, value); }
    public string City { get => _city; set => SetProperty(ref _city, value); }
    public string State { get => _state; set => SetProperty(ref _state, value); }
    public string PostalCode { get => _postalCode; set => SetProperty(ref _postalCode, value); }

    private EnquiryStage _stage;
    public EnquiryStage Stage
    {
        get => _stage;
        set
        {
            if (SetProperty(ref _stage, value)) OnPropertyChanged(nameof(IsLost));
        }
    }

    public bool IsLost => _stage == EnquiryStage.Lost;

    private string _source, _owner, _expectedValue, _requirements, _lostReason;
    public string Source { get => _source; set => SetProperty(ref _source, value); }
    public string Owner { get => _owner; set => SetProperty(ref _owner, value); }
    public string ExpectedValue { get => _expectedValue; set => SetProperty(ref _expectedValue, value); }
    public string Requirements { get => _requirements; set => SetProperty(ref _requirements, value); }
    public string LostReason { get => _lostReason; set => SetProperty(ref _lostReason, value); }

    private DateTime? _expectedClose, _followUp;
    public DateTime? ExpectedClose { get => _expectedClose; set => SetProperty(ref _expectedClose, value?.Date); }
    public DateTime? FollowUp { get => _followUp; set => SetProperty(ref _followUp, value?.Date); }

    public static IReadOnlyList<EnquiryStage> Stages { get; } = Enum.GetValues<EnquiryStage>();
    public static IReadOnlyList<string> Titles { get; } = new[] { "Mr.", "Ms.", "Mrs.", "Dr.", "M/s." };
    public static IReadOnlyList<string> Sources => Enquiry.Sources;

    /// <summary>What is missing on a step, or null.</summary>
    public string? Problem(int step)
    {
        if (step == 1)
        {
            if (string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName) && string.IsNullOrWhiteSpace(Company))
                return "Enter the client's name or company.";
            if (string.IsNullOrWhiteSpace(Phone) && string.IsNullOrWhiteSpace(Email))
                return "Enter a phone number or an e-mail address, so the client can be reached.";
            // The quote checks the client the same way: what is accepted here is never refused when the quote is ordered.
            return QuoteEditor.ClientProblem(ToEnquiry().Client);
        }
        if (ExpectedValue.Trim().Length > 0
            && (!decimal.TryParse(ExpectedValue.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) || v < 0))
            return "Enter the expected value as a number (or leave it empty).";
        return null;
    }

    public Enquiry ToEnquiry()
    {
        var e = Original.Copy();
        e.Client = new ClientInfo
        {
            Title = ClientTitle.Trim(), FirstName = FirstName.Trim(), LastName = LastName.Trim(), Company = Company.Trim(), Phone = Phone.Trim(),
            Email = Email.Trim(), AddressLine1 = AddressLine1.Trim(), AddressLine2 = AddressLine2.Trim(), City = City.Trim(), State = State.Trim(),
            PostalCode = PostalCode.Trim(), Country = Original.Client.Country, Gstin = Original.Client.Gstin
        };
        e.Stage = Stage;
        e.Source = Source.Trim();
        e.Owner = Owner.Trim();
        e.ExpectedValue = decimal.TryParse(ExpectedValue.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
        e.ExpectedCloseDate = ExpectedClose;
        e.FollowUpDate = FollowUp;
        e.Requirements = Requirements.Trim();
        e.LostReason = Stage == EnquiryStage.Lost ? LostReason.Trim() : "";
        return e;
    }
}

/// <summary>
/// The Enquiries page (Sales): every enquiry with its stage, source, salesperson, value, follow-up and quote; Open /
/// Quoted / Won / Lost / All and search; the two-step form; "Create quote" turns an enquiry into a quote with its client
/// and requirements, and the quote's result becomes the enquiry's.
/// </summary>
public sealed class EnquiriesViewModel : ViewModelBase
{
    private readonly Func<SqliteEnquiryRepository?> _repository;
    private readonly Func<Enquiry, string?> _createQuote;
    private readonly Func<Guid, string?> _openQuote;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<string> _currentUser;
    private readonly Func<DateTime> _today;
    private IReadOnlyList<EnquirySummary> _all = Array.Empty<EnquirySummary>();

    /// <param name="createQuote">Opens a new quote for the (saved) enquiry; returns an error or null.</param>
    /// <param name="openQuote">Opens the quote made from an enquiry; returns an error or null.</param>
    public EnquiriesViewModel(Func<SqliteEnquiryRepository?> repository, Func<Enquiry, string?> createQuote, Func<Guid, string?> openQuote,
        Func<IDialogService?> dialogs, Func<string> currentUser, Func<DateTime>? today = null)
    {
        _repository = repository;
        _createQuote = createQuote;
        _openQuote = openQuote;
        _dialogs = dialogs;
        _currentUser = currentUser;
        _today = today ?? (() => DateTime.Today);
        NewCommand = new RelayCommand(() =>
        {
            if (!EnquiriesAllowed)
            {
                Message = "Enquiries are not in your package: start a New quote, or ask your MARK supplier.";
                return;
            }
            Edit(new Enquiry { Owner = _currentUser(), Source = "" });
        });
        EditCommand = new RelayCommand(p =>
        {
            if (p is not EnquiryRow row) return;
            if (row.QuoteOnly) Message = _openQuote(row.Id);                  // a quote: open it
            else Open(row.Id);
        });
        NewQuoteCommand = new RelayCommand(() => NewQuote?.Invoke());
        DeleteQuoteCommand = new RelayCommand(p => { if (p is EnquiryRow { QuoteOnly: true } row) DeleteQuote(row); });
        NextCommand = new RelayCommand(Next, () => Editor is { IsStep1: true });
        BackCommand = new RelayCommand(() => { if (Editor is not null) Editor.Step = 1; }, () => Editor is { IsStep2: true });
        SaveCommand = new RelayCommand(() => Save(), () => Editor is not null);
        CancelCommand = new RelayCommand(() => Editor = null);
        DeleteCommand = new RelayCommand(Delete, () => Editor is { IsNew: false });
        CreateQuoteCommand = new RelayCommand(CreateQuote, () => Editor is not null && Editor.Original.QuoteId is null);
        OpenQuoteCommand = new RelayCommand(p =>
        {
            var id = p is EnquiryRow row ? row.Summary.QuoteId : Editor?.Original.QuoteId;
            if (id is { } quoteId) Message = _openQuote(quoteId);
        });
    }

    public ICommand NewCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CreateQuoteCommand { get; }
    public ICommand OpenQuoteCommand { get; }
    public ICommand NewQuoteCommand { get; }
    public ICommand DeleteQuoteCommand { get; }

    // ── Quotes in the same list ─────────────────────────────────────

    /// <summary>The saved quotes: those that did not come from an enquiry are listed too. Set by the main view model.</summary>
    public Func<Mark.Data.IProjectRepository?>? Quotes { get; set; }

    /// <summary>The open quote (it cannot be deleted from the list).</summary>
    public Func<Guid>? OpenQuoteId { get; set; }

    /// <summary>Starts a new quote. Set by the main view model.</summary>
    public Action? NewQuote { get; set; }

    /// <summary>The package has enquiries (else the list shows quotes only, and New enquiry says why).</summary>
    public Func<bool>? CanUseEnquiries { get; set; }

    public bool EnquiriesAllowed => CanUseEnquiries?.Invoke() ?? true;

    private HashSet<Guid> _quoteOnly = new();
    private Dictionary<Guid, decimal?> _quoteValues = new();

    private static EnquiryStage StageOf(QuoteStatus status) => status switch
    {
        QuoteStatus.Won => EnquiryStage.Won,
        QuoteStatus.Lost => EnquiryStage.Lost,
        _ => EnquiryStage.Quoted
    };

    private void DeleteQuote(EnquiryRow row)
    {
        if (Blocked?.Invoke() is { } blocked)
        {
            Message = blocked;
            return;
        }
        if (Quotes?.Invoke() is not { } repository) return;
        if (OpenQuoteId?.Invoke() == row.Id)
        {
            Message = "This quote is open. Start a new quote or open another one first to delete it.";
            return;
        }
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Delete quote", $"Delete {row.Number} '{row.Client}' permanently?"))
            return;
        try
        {
            repository.Delete(row.Id);
            Reload();
            Message = $"Deleted {row.Number}.";
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
    }

    /// <summary>Why enquiries cannot be changed now (read-only), or null. Set by the main view model.</summary>
    public Func<string?>? Blocked { get; set; }

    public ObservableCollection<EnquiryRow> Rows { get; } = new();

    /// <summary>The salespeople already used (for the Owner box).</summary>
    public ObservableCollection<string> Owners { get; } = new();

    private EnquiryFilter _filter = EnquiryFilter.All;                // everything at first: nothing looks missing
    public EnquiryFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value)) ApplyFilter();
        }
    }

    private string? _search;
    public string? Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value)) ApplyFilter();
        }
    }

    public string OpenHeader => $"Open ({_all.Count(e => IsOpen(e.Stage))})";
    public string QuotedHeader => $"Quoted ({_all.Count(e => e.Stage == EnquiryStage.Quoted)})";
    public string WonHeader => $"Won ({_all.Count(e => e.Stage == EnquiryStage.Won)})";
    public string LostHeader => $"Lost ({_all.Count(e => e.Stage == EnquiryStage.Lost)})";
    public string AllHeader => $"All ({_all.Count})";

    /// <summary>"3 follow-ups due" (today or earlier), or "".</summary>
    public string DueText
    {
        get
        {
            int due = Rows.Count == 0 ? 0 : _all.Select(e => new EnquiryRow(e, _today())).Count(r => r.IsDue);
            return due == 0 ? "" : $"{due} follow-up{(due == 1 ? "" : "s")} due";
        }
    }

    private EnquiryEditorViewModel? _editor;
    public EnquiryEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value)) return;
            OnPropertyChanged(nameof(HasEditor));
            foreach (var c in new[] { NextCommand, BackCommand, SaveCommand, DeleteCommand, CreateQuoteCommand })
                ((RelayCommand)c).RaiseCanExecuteChanged();
        }
    }

    public bool HasEditor => _editor is not null;

    private string? _message;
    public string? Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    public void Reload()
    {
        if (_repository() is not { } repository)
        {
            _all = Array.Empty<EnquirySummary>();
            Message = "There is no local database, so enquiries cannot be kept.";
        }
        else
        {
            try
            {
                _all = repository.List();
            }
            catch (DataStoreException ex)
            {
                _all = Array.Empty<EnquirySummary>();
                Message = ex.Message;
            }
        }
        // Every quote is in the list: on its enquiry's row, or as a row of its own.
        _quoteOnly = new HashSet<Guid>();
        _quoteValues = new Dictionary<Guid, decimal?>();
        if (!EnquiriesAllowed) _all = Array.Empty<EnquirySummary>();                 // not in the package: quotes only
        try
        {
            if (Quotes?.Invoke() is { } quotes)
            {
                var linked = _all.Where(e => e.QuoteId is not null).Select(e => e.QuoteId!.Value).ToHashSet();
                var rows = _all.ToList();
                foreach (var q in quotes.List())
                {
                    _quoteValues[q.Id] = q.Value;
                    if (linked.Contains(q.Id)) continue;
                    string number = string.IsNullOrEmpty(q.QuoteNumber) ? "—" : q.NumberText;
                    rows.Add(new EnquirySummary(q.Id, number, q.CreatedUtc, q.CreatedBy, q.ClientName.Length > 0 ? q.ClientName : q.Name,
                        q.ClientCity, "", StageOf(q.Status), "", q.CreatedBy, null, null, q.Id, number));
                    _quoteOnly.Add(q.Id);
                }
                _all = rows.OrderByDescending(e => e.CreatedUtc).ToList();
            }
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
        OnPropertyChanged(nameof(EnquiriesAllowed));
        Owners.Clear();
        foreach (string owner in _all.Select(e => e.Owner).Append(_currentUser()).Where(o => o.Length > 0).Distinct().Order())
            Owners.Add(owner);
        foreach (string header in new[] { nameof(OpenHeader), nameof(QuotedHeader), nameof(WonHeader), nameof(LostHeader), nameof(AllHeader) })
            OnPropertyChanged(header);
        ApplyFilter();
    }

    /// <summary>The list is empty: <see cref="EmptyText"/> says why and what to do.</summary>
    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => _all.Count == 0
        ? "No enquiries or quotes yet. Start with New enquiry (a client who asked) or New quote."
        : $"Nothing here. All ({_all.Count}) shows every enquiry and quote.";

    private static bool IsOpen(EnquiryStage stage) => stage is EnquiryStage.New or EnquiryStage.Contacted or EnquiryStage.SiteVisit;

    private void ApplyFilter()
    {
        Rows.Clear();
        var words = (Search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var e in _all)
        {
            bool keep = Filter switch
            {
                EnquiryFilter.Open => IsOpen(e.Stage),
                EnquiryFilter.Quoted => e.Stage == EnquiryStage.Quoted,
                EnquiryFilter.Won => e.Stage == EnquiryStage.Won,
                EnquiryFilter.Lost => e.Stage == EnquiryStage.Lost,
                _ => true
            };
            string haystack = $"{e.Number} {e.ClientName} {e.City} {e.Phone} {e.Source} {e.Owner} {e.QuoteNumber}";
            if (keep && words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase)))
                Rows.Add(new EnquiryRow(e, _today(), _quoteOnly.Contains(e.Id),
                    e.QuoteId is { } quoteId && _quoteValues.TryGetValue(quoteId, out var value) ? value : null));
        }
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void Edit(Enquiry enquiry)
    {
        Message = null;
        Editor = new EnquiryEditorViewModel(enquiry);
    }

    private void Open(Guid id)
    {
        try
        {
            if (_repository() is { } repository) Edit(repository.Load(id));
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
    }

    private void Next()
    {
        if (Editor is not { } editor) return;
        if (editor.Problem(1) is { } problem)
        {
            Message = problem;
            return;
        }
        Message = null;
        editor.Step = 2;
        ((RelayCommand)NextCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BackCommand).RaiseCanExecuteChanged();
    }

    /// <summary>Saves the form; returns the saved enquiry, or null with <see cref="Message"/> saying why.</summary>
    public Enquiry? Save()
    {
        if (Editor is not { } editor || _repository() is not { } repository) return null;
        if (Blocked?.Invoke() is { } blocked)
        {
            Message = blocked;
            return null;
        }
        foreach (int step in new[] { 1, 2 })
        {
            if (editor.Problem(step) is { } problem)
            {
                editor.Step = step;
                Message = problem;
                return null;
            }
        }
        var enquiry = editor.ToEnquiry();
        try
        {
            repository.Save(enquiry);
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
            return null;
        }
        Editor = null;
        Reload();
        Message = $"Saved enquiry {enquiry.Number} ({enquiry.Client.DisplayName}).";
        return enquiry;
    }

    private void CreateQuote()
    {
        if (Save() is not { } enquiry) return;
        Message = _createQuote(enquiry);
    }

    private void Delete()
    {
        if (Editor?.Original is not { } enquiry || _repository() is not { } repository) return;
        if (Blocked?.Invoke() is { } blocked)
        {
            Message = blocked;
            return;
        }
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Delete enquiry",
                $"Delete enquiry {enquiry.Number} ({enquiry.Client.DisplayName})? A quote made from it stays."))
            return;
        try
        {
            repository.Delete(enquiry.Id);
            Editor = null;
            Reload();
            Message = $"Deleted enquiry {enquiry.Number}.";
        }
        catch (DataStoreException ex)
        {
            Message = ex.Message;
        }
    }
}
