using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Core.Models;
using Mark.Core.Orders;
using Mark.Core.Production;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>An order in the list: "OR-00012 · Sharma residence", its client and value, stage and how much is paid.</summary>
public sealed record OrderRow(Guid Id, string OrderNumber, string Title, string Detail, OrderStage Stage, string StageText,
    string PaidText, double PaidFraction, bool HasBalance)
{
    public bool IsOpen => Stage != OrderStage.Closed;
}

/// <summary>A stage in the order's stage bar.</summary>
public sealed record StageStep(OrderStage Stage, string Name, bool IsDone, bool IsCurrent, string DateText);

/// <summary>A payment of the open order.</summary>
public sealed record PaymentRow(Guid Id, string DateText, string KindText, string MethodText, string Reference, string Note, string AmountText,
    string RecordedBy);

/// <summary>A delivery, installation or site visit (of the open order, or of every order on the Schedule tab).</summary>
public sealed record VisitRow(Guid OrderId, Guid Id, string OrderNumber, string Title, DateTime Date, string DateText, string KindText,
    string Time, string Team, string Note, bool Done, bool IsLate, string SiteAddress);

/// <summary>A dispatch note of the open order.</summary>
public sealed record DispatchRow(Guid Id, string Number, string DateText, string Detail, int WindowCount);

/// <summary>A design of the open order on the next dispatch note: how many there are, have gone, and go now.</summary>
public sealed class DispatchLineRow : ViewModelBase
{
    public DispatchLineRow(Guid frameId, string reference, string description, string sizeText, int quantity, int dispatched)
    {
        FrameId = frameId;
        Reference = reference;
        Description = description;
        SizeText = sizeText;
        Quantity = quantity;
        Dispatched = dispatched;
        _sendText = Remaining.ToString(CultureInfo.InvariantCulture);
    }

    public Guid FrameId { get; }
    public string Reference { get; }
    public string Description { get; }
    public string SizeText { get; }
    public int Quantity { get; }
    public int Dispatched { get; }
    public int Remaining => Math.Max(0, Quantity - Dispatched);
    public string StatusText => Dispatched == 0 ? $"{Quantity} to go" : Remaining == 0 ? "all gone" : $"{Dispatched} of {Quantity} gone";

    private string _sendText;
    /// <summary>How many go on this dispatch note (text, so a wrong entry can be shown).</summary>
    public string SendText { get => _sendText; set => SetProperty(ref _sendText, value ?? ""); }
}

/// <summary>A value of an enum with its name, for a drop-down.</summary>
public sealed record EnumChoice(object Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Company details printed on order papers.</summary>
public sealed record Letterhead(string Name, string Address, string Phone);

/// <summary>
/// Orders › Orders and Schedule (Milestone 17): every order (a quote converted to an order) from confirmation to
/// installation. The chosen order shows its stage (Confirmed → In production → Ready → Dispatched → Installed → Closed),
/// its value, the payments received and the balance, its delivery and installation schedule, its dispatch notes (PDF,
/// numbered DN-00001…), and the client's installation sign-off (PDF certificate). Production, dispatch and sign-off move
/// the stage forward on their own; it can also be set by hand. The Schedule tab lists the visits of every order.
/// </summary>
public sealed class OrdersViewModel : ViewModelBase
{
    private readonly Func<LocalStore?> _store;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<Letterhead> _letterhead;
    private readonly Func<string> _user;
    private readonly Func<DateTime> _now;
    private CustomerOrder? _order;
    private Project? _project;

    public OrdersViewModel(Func<LocalStore?> store, Func<IDialogService?> dialogs, Func<Letterhead> letterhead, Func<string> user,
        Func<DateTime>? now = null)
    {
        _store = store;
        _dialogs = dialogs;
        _letterhead = letterhead;
        _user = user;
        _now = now ?? (() => DateTime.Now);
        SetStageCommand = new RelayCommand(p => Report(SetStage(p is OrderStage s ? s : Enum.Parse<OrderStage>(p?.ToString() ?? ""))),
            _ => _order is not null && CanChange);
        AddPaymentCommand = new RelayCommand(() => Report(AddPayment()), () => _order is not null && CanChange);
        RemovePaymentCommand = new RelayCommand(p => Report(RemovePayment(p is PaymentRow r ? r.Id : Guid.Empty)), _ => CanChange);
        AddVisitCommand = new RelayCommand(() => Report(AddVisit()), () => _order is not null && CanChange);
        RemoveVisitCommand = new RelayCommand(p => Report(RemoveVisit(p as VisitRow)), _ => CanChange);
        ToggleVisitCommand = new RelayCommand(p => Report(ToggleVisit(p as VisitRow)), _ => CanChange);
        CreateDispatchCommand = new RelayCommand(() => Report(CreateDispatch()), () => _order is not null && CanChange);
        DispatchPdfCommand = new RelayCommand(p => Report(DispatchPdf(p is DispatchRow r ? r.Id : Guid.Empty)), _ => _order is not null);
        RemoveDispatchCommand = new RelayCommand(p => Report(RemoveDispatch(p is DispatchRow r ? r.Id : Guid.Empty)), _ => CanChange);
        SaveSignOffCommand = new RelayCommand(() => Report(SaveSignOff()), () => _order is not null && CanChange);
        CertificateCommand = new RelayCommand(() => Report(Certificate()), () => _order?.SignOff is not null);
        OpenQuoteCommand = new RelayCommand(() => { if (_order is not null) OpenQuote?.Invoke(_order.ProjectId); }, () => _order is not null);
        OpenProductionCommand = new RelayCommand(() => { if (_order is not null) OpenProduction?.Invoke(_order.ProjectId); }, () => _order is not null);
        OpenVisitOrderCommand = new RelayCommand(p => { if (p is VisitRow v) ShowOrder(v.OrderId); });
    }

    /// <summary>Why changes are refused (read-only licence, or the feature not given), or null.</summary>
    public Func<string?> Blocked { get; set; } = () => null;

    /// <summary>Opens a written paper with the computer's PDF viewer.</summary>
    public Action<string>? OpenDocument { get; set; }

    /// <summary>Opens the order's quote in Sales.</summary>
    public Action<Guid>? OpenQuote { get; set; }

    /// <summary>Opens (or starts) the order's production order.</summary>
    public Action<Guid>? OpenProduction { get; set; }

    /// <summary>Shows the Orders tab (from the Schedule).</summary>
    public Action? ShowOrdersTab { get; set; }

    public bool CanChange => Blocked() is null;

    public ObservableCollection<OrderRow> Orders { get; } = new();
    public ObservableCollection<StageStep> StageSteps { get; } = new();
    public ObservableCollection<PaymentRow> Payments { get; } = new();
    public ObservableCollection<VisitRow> Visits { get; } = new();
    public ObservableCollection<DispatchRow> Dispatches { get; } = new();
    public ObservableCollection<DispatchLineRow> DispatchLines { get; } = new();
    public ObservableCollection<VisitRow> Schedule { get; } = new();

    public bool HasOrders => Orders.Count > 0;
    public bool HasOrder => _order is not null;

    private List<OrderRow> _all = new();

    public static IReadOnlyList<string> Filters { get; } = new[] { "Open orders", "All orders", "With a balance", "Closed" };

    private string _filter = Filters[0];
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? Filters[0])) ApplyFilter();
        }
    }

    private string _search = "";
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? "")) ApplyFilter();
        }
    }

    public string ListSummary { get; private set; } = "";

    private OrderRow? _selected;
    private bool _replacingRow;
    public OrderRow? Selected
    {
        get => _selected;
        set
        {
            if (_replacingRow) return;
            if (!SetProperty(ref _selected, value)) return;
            if (value is not null) Open(value.Id);
        }
    }

    // ── The open order ──────────────────────────────────────────────

    public string OrderTitle => _order is null ? "" : $"{_order.OrderNumber}  ·  {_order.ProjectName}";

    public string OrderDetail => _order is null ? ""
        : string.Join("  ·  ", new[]
        {
            _order.QuoteNumber, _order.ClientName, _order.ClientPhone,
            _order.ConfirmedUtc == default ? "" : $"confirmed {Day(_order.ConfirmedUtc.ToLocalTime())}"
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string SiteText => _order is null ? "" : _order.SiteAddress.Length > 0 ? _order.SiteAddress : "No site address on the quote's client.";

    public string StageText => _order is null ? "" : CustomerOrder.StageName(_order.Stage);

    public string ValueText => _order?.Value is { } v ? Money(v) : "Not priced";
    public string PaidText => _order is null ? "" : Money(_order.Paid);
    public string BalanceText => _order?.Balance is { } b ? Money(b) : "—";
    public double PaidFraction => _order?.PaidFraction ?? 0;
    public string PaidPercentText => _order?.Value is > 0 ? $"{PaidFraction * 100:0} % received" : "";

    public string ProductionText { get; private set; } = "";

    public bool HasSignOff => _order?.SignOff is not null;

    public string SignOffText => _order?.SignOff is { } s
        ? $"Signed off on {Day(s.Date)} by {(s.SignedBy.Length > 0 ? s.SignedBy : "the client")}" + (s.InstalledBy.Length > 0 ? $"; installed by {s.InstalledBy}" : "") + "."
        : "Not signed off yet.";

    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value ?? "")) SaveNotes();
        }
    }

    public void SaveNotes()
    {
        if (_order is null || _order.Notes == _notes.Trim()) return;
        _order.Notes = _notes.Trim();
        Report(Save());
    }

    // ── Forms ───────────────────────────────────────────────────────

    public static IReadOnlyList<EnumChoice> PaymentKinds { get; } =
        Enum.GetValues<PaymentKind>().Select(k => new EnumChoice(k, CustomerOrder.KindName(k))).ToList();
    public static IReadOnlyList<EnumChoice> PaymentMethods { get; } =
        Enum.GetValues<PaymentMethod>().Select(m => new EnumChoice(m, CustomerOrder.MethodName(m))).ToList();
    public static IReadOnlyList<EnumChoice> VisitKinds { get; } =
        Enum.GetValues<VisitKind>().Select(k => new EnumChoice(k, CustomerOrder.VisitName(k))).ToList();

    private DateTime? _paymentDate;
    public DateTime? PaymentDate { get => _paymentDate; set => SetProperty(ref _paymentDate, value); }
    private string _paymentAmount = "";
    public string PaymentAmount { get => _paymentAmount; set => SetProperty(ref _paymentAmount, value ?? ""); }
    private PaymentKind _paymentKind = PaymentKind.Advance;
    public PaymentKind PaymentKind { get => _paymentKind; set => SetProperty(ref _paymentKind, value); }
    private PaymentMethod _paymentMethod = PaymentMethod.BankTransfer;
    public PaymentMethod PaymentMethod { get => _paymentMethod; set => SetProperty(ref _paymentMethod, value); }
    private string _paymentReference = "";
    public string PaymentReference { get => _paymentReference; set => SetProperty(ref _paymentReference, value ?? ""); }

    private VisitKind _visitKind = VisitKind.Delivery;
    public VisitKind VisitKind { get => _visitKind; set => SetProperty(ref _visitKind, value); }
    private DateTime? _visitDate;
    public DateTime? VisitDate { get => _visitDate; set => SetProperty(ref _visitDate, value); }
    private string _visitTime = "";
    public string VisitTime { get => _visitTime; set => SetProperty(ref _visitTime, value ?? ""); }
    private string _visitTeam = "";
    public string VisitTeam { get => _visitTeam; set => SetProperty(ref _visitTeam, value ?? ""); }
    private string _visitNote = "";
    public string VisitNote { get => _visitNote; set => SetProperty(ref _visitNote, value ?? ""); }

    private DateTime? _dispatchDate;
    public DateTime? DispatchDate { get => _dispatchDate; set => SetProperty(ref _dispatchDate, value); }
    private string _vehicle = "";
    public string Vehicle { get => _vehicle; set => SetProperty(ref _vehicle, value ?? ""); }
    private string _driver = "";
    public string Driver { get => _driver; set => SetProperty(ref _driver, value ?? ""); }
    private string _dispatchNote = "";
    public string DispatchNoteText { get => _dispatchNote; set => SetProperty(ref _dispatchNote, value ?? ""); }

    public bool HasDispatchLeft => DispatchLines.Any(l => l.Remaining > 0);

    private DateTime? _signOffDate;
    public DateTime? SignOffDate { get => _signOffDate; set => SetProperty(ref _signOffDate, value); }
    private string _signedBy = "";
    public string SignedBy { get => _signedBy; set => SetProperty(ref _signedBy, value ?? ""); }
    private string _installedBy = "";
    public string InstalledBy { get => _installedBy; set => SetProperty(ref _installedBy, value ?? ""); }
    private string _remarks = "";
    public string Remarks { get => _remarks; set => SetProperty(ref _remarks, value ?? ""); }

    // ── Schedule tab ────────────────────────────────────────────────

    public static IReadOnlyList<string> ScheduleFilters { get; } = new[] { "Coming up", "All visits", "Done" };

    private string _scheduleFilter = ScheduleFilters[0];
    public string ScheduleFilter
    {
        get => _scheduleFilter;
        set
        {
            if (SetProperty(ref _scheduleFilter, value ?? ScheduleFilters[0])) FillSchedule();
        }
    }

    public bool HasSchedule => Schedule.Count > 0;

    public string ScheduleSummary { get; private set; } = "";

    // ── Commands ────────────────────────────────────────────────────

    public ICommand SetStageCommand { get; }
    public ICommand AddPaymentCommand { get; }
    public ICommand RemovePaymentCommand { get; }
    public ICommand AddVisitCommand { get; }
    public ICommand RemoveVisitCommand { get; }
    public ICommand ToggleVisitCommand { get; }
    public ICommand CreateDispatchCommand { get; }
    public ICommand DispatchPdfCommand { get; }
    public ICommand RemoveDispatchCommand { get; }
    public ICommand SaveSignOffCommand { get; }
    public ICommand CertificateCommand { get; }
    public ICommand OpenQuoteCommand { get; }
    public ICommand OpenProductionCommand { get; }
    public ICommand OpenVisitOrderCommand { get; }

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

    // ── Loading ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads every order: each quote with an order number gets its order record (made the first time), kept up to date
    /// with the quote (number, client, value) and with production (the stage moves forward when production starts or
    /// every window is ready).
    /// </summary>
    public void Reload(Guid? select = null)
    {
        if (_store() is not { } store) return;
        var keep = select ?? _selected?.Id;
        try
        {
            var records = store.Orders.List().ToDictionary(o => o.ProjectId);
            var production = store.Production.List().ToDictionary(p => p.ProjectId, p => p);
            var rows = new List<OrderRow>();
            foreach (var p in store.Projects.List().Where(p => p.OrderNumber.Length > 0))
            {
                bool isNew = !records.TryGetValue(p.Id, out var order);
                order ??= new CustomerOrder { ProjectId = p.Id, ConfirmedUtc = p.DecidedUtc ?? p.ModifiedUtc };
                bool changed = isNew | Sync(order, p);
                if (isNew) order.SetStage(OrderStage.Confirmed, order.ConfirmedUtc, p.ModifiedBy);
                if (production.TryGetValue(p.Id, out var job)) changed |= FollowProduction(order, job, store);
                if (changed) store.Orders.Save(order);
                rows.Add(RowOf(order));
            }
            _all = rows.OrderByDescending(r => r.OrderNumber, StringComparer.Ordinal).ToList();
            ApplyFilter(keep);
            FillSchedule(store.Orders.List());
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    /// <summary>Opens an order (from the Schedule, or another area) on the Orders tab.</summary>
    public void ShowOrder(Guid orderId)
    {
        ShowOrdersTab?.Invoke();
        if (_filter != Filters[1] && _all.FirstOrDefault(r => r.Id == orderId) is { } row && !Matches(row, _filter, ""))
            _filter = Filters[1];
        _search = "";
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Search));
        ApplyFilter(orderId);
    }

    /// <summary>The order of a quote, opened on the Orders tab (made when it has none yet). Returns an error, or null.</summary>
    public string? ShowProject(Guid projectId)
    {
        Reload();
        if (_store()?.Orders.ForProject(projectId) is not { } order) return "Convert the quote to an order first.";
        ShowOrder(order.Id);
        return null;
    }

    private static bool Sync(CustomerOrder order, ProjectSummary p)
    {
        bool changed = order.OrderNumber != p.OrderNumber || order.QuoteNumber != p.NumberText || order.ProjectName != p.Name
                       || order.ClientName != p.ClientName || order.Value != p.Value || order.Currency != p.Currency;
        order.OrderNumber = p.OrderNumber;
        order.QuoteNumber = p.NumberText;
        order.ProjectName = p.Name;
        order.ClientName = p.ClientName;
        order.Value = p.Value;
        order.Currency = p.Currency;
        return changed;
    }

    /// <summary>Production started: In production; every window ready (or further): Ready. Only ever forward.</summary>
    private bool FollowProduction(CustomerOrder order, ProductionOrder job, LocalStore store)
    {
        var target = OrderStage.InProduction;
        try
        {
            var full = store.Production.Load(job.Id);
            var quantities = ProjectSerializer.Deserialize(full.DocumentJson).Frames.ToDictionary(f => f.Id, f => Math.Max(1, f.Design.Quantity));
            if (quantities.Count > 0 && quantities.All(q => full.ProgressOf(q.Key).DoneAt(ProductionStep.Ready) >= q.Value))
                target = OrderStage.Ready;
        }
        catch (Exception ex) when (ex is DataStoreException or InvalidOperationException or System.Text.Json.JsonException)
        {
        }
        if (order.Stage >= target) return false;
        order.SetStage(target, DateTime.UtcNow, "");
        return true;
    }

    private static OrderRow RowOf(CustomerOrder o)
        => new(o.Id, o.OrderNumber, o.ProjectName.Length > 0 ? o.ProjectName : "Order",
            string.Join("  ·  ", new[] { o.ClientName, o.Value is { } v ? Money(v) : "" }.Where(t => t.Length > 0)),
            o.Stage, CustomerOrder.StageName(o.Stage),
            o.Value is > 0 ? (o.Balance > 0 ? $"{Money(o.Balance!.Value)} due" : "Paid") : o.Paid > 0 ? $"{Money(o.Paid)} received" : "",
            o.PaidFraction, o.Balance > 0);

    private static bool Matches(OrderRow r, string filter, string search)
    {
        bool inFilter = filter switch
        {
            "All orders" => true,
            "With a balance" => r.HasBalance,
            "Closed" => r.Stage == OrderStage.Closed,
            _ => r.IsOpen
        };
        var words = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string text = $"{r.OrderNumber} {r.Title} {r.Detail} {r.StageText}";
        return inFilter && words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilter(Guid? keep = null)
    {
        keep ??= _selected?.Id;
        Orders.Clear();
        foreach (var row in _all.Where(r => Matches(r, _filter, _search))) Orders.Add(row);
        ListSummary = _all.Count == 0 ? "" : $"{Orders.Count} of {_all.Count} orders";
        OnPropertyChanged(nameof(ListSummary));
        OnPropertyChanged(nameof(HasOrders));
        _selected = Orders.FirstOrDefault(o => o.Id == keep) ?? Orders.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        Open(_selected?.Id);
    }

    private void Open(Guid? id)
    {
        _order = null;
        _project = null;
        if (id is { } orderId && _store() is { } store)
        {
            try
            {
                _order = store.Orders.Load(orderId);
                _project = store.Projects.Load(_order.ProjectId);
                string phone = _project.Quote.Client.Phone.Trim();
                string site = _project.Quote.Client.AddressText;
                if (_order.ClientPhone != phone || _order.SiteAddress != site)
                {
                    _order.ClientPhone = phone;
                    _order.SiteAddress = site;
                    store.Orders.Save(_order);
                }
                ProductionText = ProductionTextOf(store, _order.ProjectId);
            }
            catch (DataStoreException ex)
            {
                Show($"The order could not be opened: {ex.Message}", true);
                _order = null;
                _project = null;
            }
        }
        _notes = _order?.Notes ?? "";
        ResetForms();
        Refresh();
    }

    private static string ProductionTextOf(LocalStore store, Guid projectId)
    {
        if (store.Production.ForProject(projectId) is not { } id) return "Not in production yet.";
        var job = store.Production.Load(id);
        var quantities = ProjectSerializer.Deserialize(job.DocumentJson).Frames.ToDictionary(f => f.Id, f => Math.Max(1, f.Design.Quantity));
        return $"Production: {job.StageText(quantities)} · {job.Fraction(quantities) * 100:0} % done"
               + (job.DueDate is { } due ? $" · due {Day(due)}" : "");
    }

    private void ResetForms()
    {
        var today = _now().Date;
        _paymentDate = today;
        _paymentAmount = "";
        _paymentReference = "";
        _paymentKind = _order is { Payments.Count: 0 } ? PaymentKind.Advance : _order?.Balance is { } b && b > 0 ? PaymentKind.Stage : PaymentKind.Other;
        _visitDate = today.AddDays(1);
        _visitTime = "";
        _visitTeam = "";
        _visitNote = "";
        _visitKind = _order is { Stage: >= OrderStage.Dispatched } ? VisitKind.Installation : VisitKind.Delivery;
        _dispatchDate = today;
        _vehicle = "";
        _driver = "";
        _dispatchNote = "";
        var s = _order?.SignOff;
        _signOffDate = s?.Date ?? today;
        _signedBy = s?.SignedBy ?? _order?.ClientName ?? "";
        _installedBy = s?.InstalledBy ?? "";
        _remarks = s?.Remarks ?? "";
        foreach (string name in new[]
                 {
                     nameof(PaymentDate), nameof(PaymentAmount), nameof(PaymentReference), nameof(PaymentKind), nameof(VisitDate), nameof(VisitTime),
                     nameof(VisitTeam), nameof(VisitNote), nameof(VisitKind), nameof(DispatchDate), nameof(Vehicle), nameof(Driver),
                     nameof(DispatchNoteText), nameof(SignOffDate), nameof(SignedBy), nameof(InstalledBy), nameof(Remarks)
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Fills the open order's parts and the header from the model.</summary>
    private void Refresh()
    {
        StageSteps.Clear();
        Payments.Clear();
        Visits.Clear();
        Dispatches.Clear();
        DispatchLines.Clear();
        if (_order is { } o)
        {
            foreach (var stage in CustomerOrder.Stages)
                StageSteps.Add(new StageStep(stage, CustomerOrder.StageName(stage), stage <= o.Stage, stage == o.Stage,
                    o.ReachedUtc(stage) is { } at && stage <= o.Stage ? Day(at.ToLocalTime()) : ""));
            foreach (var p in o.Payments.OrderBy(p => p.Date))
                Payments.Add(new PaymentRow(p.Id, Day(p.Date), CustomerOrder.KindName(p.Kind), CustomerOrder.MethodName(p.Method), p.Reference, p.Note,
                    Money(p.Amount), p.RecordedBy));
            foreach (var v in o.Visits.OrderBy(v => v.Date))
                Visits.Add(VisitRowOf(o, v));
            foreach (var d in o.Dispatches.OrderBy(d => d.Number, StringComparer.Ordinal))
                Dispatches.Add(new DispatchRow(d.Id, d.Number, Day(d.Date),
                    string.Join("  ·  ", new[] { $"{d.WindowCount} window{(d.WindowCount == 1 ? "" : "s")}", d.Vehicle, d.Driver }.Where(t => t.Length > 0)),
                    d.WindowCount));
            if (_project is { } project)
            {
                var refs = ProductionBuilder.Windows(project);
                foreach (var f in project.Frames)
                {
                    var (reference, quantity) = refs[f.Id];
                    DispatchLines.Add(new DispatchLineRow(f.Id, reference, Describe(f), SizeOf(f), quantity, o.DispatchedOf(f.Id)));
                }
            }
        }
        foreach (string name in new[]
                 {
                     nameof(HasOrder), nameof(OrderTitle), nameof(OrderDetail), nameof(SiteText), nameof(StageText), nameof(ValueText), nameof(PaidText),
                     nameof(BalanceText), nameof(PaidFraction), nameof(PaidPercentText), nameof(ProductionText), nameof(HasSignOff), nameof(SignOffText),
                     nameof(Notes), nameof(HasDispatchLeft)
                 })
            OnPropertyChanged(name);
        foreach (var command in new[]
                 {
                     SetStageCommand, AddPaymentCommand, AddVisitCommand, CreateDispatchCommand, DispatchPdfCommand, SaveSignOffCommand,
                     CertificateCommand, OpenQuoteCommand, OpenProductionCommand
                 })
            ((RelayCommand)command).RaiseCanExecuteChanged();
    }

    private static string Describe(Frame f)
        => string.Join(" · ", new[] { f.Design.Name, f.Design.Location, f.Design.Floor.Length > 0 ? $"floor {f.Design.Floor}" : "" }
            .Where(t => !string.IsNullOrWhiteSpace(t))) is { Length: > 0 } text ? text : "Window";

    private static string SizeOf(Frame f)
        => $"{f.Width.ToString("0.#", CultureInfo.InvariantCulture)} × {f.Height.ToString("0.#", CultureInfo.InvariantCulture)} mm";

    private VisitRow VisitRowOf(CustomerOrder o, OrderVisit v)
        => new(o.Id, v.Id, o.OrderNumber, o.ProjectName, v.Date, Day(v.Date), CustomerOrder.VisitName(v.Kind), v.Time, v.Team, v.Note, v.Done,
            !v.Done && v.Date.Date < _now().Date, o.SiteAddress);

    private void FillSchedule(IReadOnlyList<CustomerOrder>? orders = null)
    {
        try
        {
            orders ??= _store()?.Orders.List() ?? Array.Empty<CustomerOrder>();
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
            return;
        }
        var today = _now().Date;
        var all = orders.SelectMany(o => o.Visits.Select(v => VisitRowOf(o, v))).ToList();
        var shown = _scheduleFilter switch
        {
            "All visits" => all.OrderBy(v => v.Date),
            "Done" => all.Where(v => v.Done).OrderByDescending(v => v.Date),
            _ => all.Where(v => !v.Done).OrderBy(v => v.Date)
        };
        Schedule.Clear();
        foreach (var v in shown) Schedule.Add(v);
        int late = all.Count(v => v.IsLate);
        int thisWeek = all.Count(v => !v.Done && v.Date.Date >= today && v.Date.Date < today.AddDays(7));
        ScheduleSummary = all.Count == 0 ? "No deliveries or installations planned yet. Plan them on an order's page."
            : $"{thisWeek} in the next 7 days" + (late > 0 ? $" · {late} overdue" : "") + $" · {all.Count(v => !v.Done)} to do";
        OnPropertyChanged(nameof(ScheduleSummary));
        OnPropertyChanged(nameof(HasSchedule));
    }

    // ── Changes ─────────────────────────────────────────────────────

    /// <summary>Sets the stage by hand (forward or back). Returns an error, or null.</summary>
    public string? SetStage(OrderStage stage)
    {
        if (_order is null) return null;
        if (Blocked() is { } blocked) return blocked;
        if (_order.Stage == stage) return null;
        if (stage == OrderStage.Closed && _order.Balance > 0 && _dialogs() is { } dialogs
            && !dialogs.Confirm("Close order", $"{_order.OrderNumber} still has {BalanceText} to receive. Close it anyway?"))
            return null;
        _order.SetStage(stage, DateTime.UtcNow, _user());
        return SaveAndRefresh($"{_order.OrderNumber} is now {CustomerOrder.StageName(stage).ToLowerInvariant()}.");
    }

    /// <summary>Records a payment from the form. Returns an error, or null.</summary>
    public string? AddPayment()
    {
        if (_order is null) return "Choose an order.";
        if (Blocked() is { } blocked) return blocked;
        if (!TryMoney(PaymentAmount, out decimal amount) || amount <= 0) return "Enter the amount received, e.g. 25000.";
        if (PaymentDate is not { } date) return "Enter the day the payment was received.";
        _order.Payments.Add(new OrderPayment
        {
            Date = date.Date, Amount = amount, Kind = PaymentKind, Method = PaymentMethod, Reference = PaymentReference.Trim(), RecordedBy = _user()
        });
        string? error = SaveAndRefresh($"Recorded {Money(amount)} ({CustomerOrder.KindName(PaymentKind).ToLowerInvariant()}). Balance {BalanceText}.");
        if (error is null) ResetForms();
        return error;
    }

    private string? RemovePayment(Guid id)
    {
        if (_order is null || _order.Payments.FirstOrDefault(p => p.Id == id) is not { } payment) return null;
        if (Blocked() is { } blocked) return blocked;
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Remove payment", $"Remove the payment of {Money(payment.Amount)} on {Day(payment.Date)}?"))
            return null;
        _order.Payments.Remove(payment);
        return SaveAndRefresh($"Removed the payment of {Money(payment.Amount)}.");
    }

    /// <summary>Adds a delivery, installation or site visit from the form. Returns an error, or null.</summary>
    public string? AddVisit()
    {
        if (_order is null) return "Choose an order.";
        if (Blocked() is { } blocked) return blocked;
        if (VisitDate is not { } date) return "Enter the day of the visit.";
        _order.Visits.Add(new OrderVisit { Kind = VisitKind, Date = date.Date, Time = VisitTime.Trim(), Team = VisitTeam.Trim(), Note = VisitNote.Trim() });
        string? error = SaveAndRefresh($"Planned the {CustomerOrder.VisitName(VisitKind).ToLowerInvariant()} on {Day(date)}.");
        if (error is null) ResetForms();
        return error;
    }

    private string? RemoveVisit(VisitRow? row)
    {
        if (row is null || _store() is not { } store) return null;
        if (Blocked() is { } blocked) return blocked;
        return Change(row.OrderId, o => o.Visits.RemoveAll(v => v.Id == row.Id) > 0, $"Removed the {row.KindText.ToLowerInvariant()} on {row.DateText}.");
    }

    private string? ToggleVisit(VisitRow? row)
    {
        if (row is null) return null;
        if (Blocked() is { } blocked) return blocked;
        return Change(row.OrderId, o =>
        {
            int i = o.Visits.FindIndex(v => v.Id == row.Id);
            if (i < 0) return false;
            o.Visits[i] = o.Visits[i] with { Done = !o.Visits[i].Done };
            return true;
        }, row.Done ? $"The {row.KindText.ToLowerInvariant()} on {row.DateText} is to do again." : $"The {row.KindText.ToLowerInvariant()} on {row.DateText} is done.");
    }

    /// <summary>Changes an order (the open one, or one on the Schedule) and saves it.</summary>
    private string? Change(Guid orderId, Func<CustomerOrder, bool> change, string done)
    {
        if (_store() is not { } store) return null;
        try
        {
            var order = _order?.Id == orderId ? _order : store.Orders.Load(orderId);
            if (!change(order)) return null;
            store.Orders.Save(order);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        Refresh();
        FillSchedule();
        UpdateListRow();
        Show(done, false);
        return null;
    }

    /// <summary>
    /// Makes a dispatch note for the windows entered (at most what is left of each design), saves it, and writes its PDF
    /// (to <paramref name="path"/>, or where the user chooses). When every window has gone, the order is Dispatched.
    /// Returns an error, or null.
    /// </summary>
    public string? CreateDispatch(string? path = null)
    {
        if (_order is null || _store() is not { } store) return "Choose an order.";
        if (Blocked() is { } blocked) return blocked;
        var lines = new List<DispatchLine>();
        foreach (var row in DispatchLines)
        {
            string text = row.SendText.Trim();
            if (text.Length == 0) continue;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int n) || n < 0)
                return $"Enter how many of {row.Reference} go as a whole number.";
            if (n > row.Remaining)
                return row.Remaining == 0 ? $"Every {row.Reference} has gone already." : $"Only {row.Remaining} of {row.Reference} are left to go.";
            if (n > 0) lines.Add(new DispatchLine { FrameId = row.FrameId, Reference = row.Reference, Description = row.Description, SizeText = row.SizeText, Quantity = n });
        }
        if (lines.Count == 0) return "Enter how many windows of each design go on this dispatch note.";
        if (DispatchDate is not { } date) return "Enter the day of dispatch.";
        DispatchNote note;
        try
        {
            note = new DispatchNote
            {
                Number = store.Orders.NextDispatchNumber(), Date = date.Date, Vehicle = Vehicle.Trim(), Driver = Driver.Trim(), Note = DispatchNoteText.Trim(),
                Lines = lines, CreatedBy = _user()
            };
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        _order.Dispatches.Add(note);
        bool allGone = DispatchLines.All(r => r.Dispatched + (lines.FirstOrDefault(l => l.FrameId == r.FrameId)?.Quantity ?? 0) >= r.Quantity);
        if (allGone && _order.Stage < OrderStage.Dispatched) _order.SetStage(OrderStage.Dispatched, DateTime.UtcNow, _user());
        if (SaveAndRefresh($"Made dispatch note {note.Number}: {note.WindowCount} window{(note.WindowCount == 1 ? "" : "s")}" +
                           (allGone ? "; every window has gone." : ".")) is { } error)
            return error;
        ResetForms();
        return WritePaper(note.Id, path);
    }

    private string? DispatchPdf(Guid id) => WritePaper(id, null);

    private string? WritePaper(Guid dispatchId, string? path)
    {
        if (_order?.Dispatches.FirstOrDefault(d => d.Id == dispatchId) is not { } note) return null;
        path ??= _dialogs()?.ChooseSaveFile("Save dispatch note", "PDF files (*.pdf)|*.pdf", FileName($"{note.Number} {_order.OrderNumber}"));
        if (path is null) return null;
        return Write(path, stream => OrderPdf.WriteDispatchNote(Paper() with
        {
            Number = note.Number, Date = note.Date, Vehicle = note.Vehicle, Driver = note.Driver, Note = note.Note,
            Lines = note.Lines.Select(l => new OrderPaperLine(l.Reference, l.Description, l.SizeText, l.Quantity)).ToList()
        }, stream), "dispatch note");
    }

    private string? RemoveDispatch(Guid id)
    {
        if (_order?.Dispatches.FirstOrDefault(d => d.Id == id) is not { } note) return null;
        if (Blocked() is { } blocked) return blocked;
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Remove dispatch note",
                $"Remove dispatch note {note.Number}? Its windows count as not dispatched again. (A printed copy stays valid on paper.)"))
            return null;
        _order.Dispatches.Remove(note);
        return SaveAndRefresh($"Removed dispatch note {note.Number}.");
    }

    /// <summary>Records the client's installation sign-off from the form; the order is Installed. Returns an error, or null.</summary>
    public string? SaveSignOff()
    {
        if (_order is null) return "Choose an order.";
        if (Blocked() is { } blocked) return blocked;
        if (SignOffDate is not { } date) return "Enter the day of the sign-off.";
        if (SignedBy.Trim().Length == 0) return "Enter who signed for the client.";
        _order.SignOff = new InstallationSignOff
        {
            Date = date.Date, SignedBy = SignedBy.Trim(), InstalledBy = InstalledBy.Trim(), Remarks = Remarks.Trim(), RecordedBy = _user()
        };
        if (_order.Stage < OrderStage.Installed) _order.SetStage(OrderStage.Installed, DateTime.UtcNow, _user());
        return SaveAndRefresh($"{_order.OrderNumber} is signed off as installed.");
    }

    /// <summary>Writes the installation certificate (to <paramref name="path"/>, or where the user chooses).</summary>
    public string? Certificate(string? path = null)
    {
        if (_order?.SignOff is not { } signOff || _project is null) return "Record the sign-off first.";
        path ??= _dialogs()?.ChooseSaveFile("Save installation certificate", "PDF files (*.pdf)|*.pdf",
            FileName($"{_order.OrderNumber} Installation certificate"));
        if (path is null) return null;
        var refs = ProductionBuilder.Windows(_project);
        return Write(path, stream => OrderPdf.WriteCertificate(Paper() with
        {
            Date = signOff.Date, SignedBy = signOff.SignedBy, InstalledBy = signOff.InstalledBy, Note = signOff.Remarks,
            Lines = _project.Frames.Select(f => new OrderPaperLine(refs[f.Id].Reference, Describe(f), SizeOf(f), refs[f.Id].Quantity)).ToList()
        }, stream), "installation certificate");
    }

    private OrderPaper Paper()
    {
        var head = _letterhead();
        return new OrderPaper
        {
            CompanyName = head.Name, CompanyAddress = head.Address, CompanyPhone = head.Phone, OrderNumber = _order!.OrderNumber,
            QuoteNumber = _order.QuoteNumber, ProjectName = _order.ProjectName, ClientName = _order.ClientName, ClientPhone = _order.ClientPhone,
            SiteAddress = _order.SiteAddress, PrintedText = $"Printed {_now().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}"
        };
    }

    private string? Write(string path, Action<Stream> write, string what)
    {
        try
        {
            using (var stream = File.Create(path))
                write(stream);
            Show($"Saved {Path.GetFileName(path)}.", false);
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The {what} could not be written: {ex.Message}";
        }
    }

    private static string FileName(string text) => string.Join("_", text.Split(Path.GetInvalidFileNameChars())).Trim() + ".pdf";

    private string? SaveAndRefresh(string done)
    {
        if (Save() is { } error) return error;
        Refresh();
        FillSchedule();
        UpdateListRow();
        Show(done, false);
        return null;
    }

    private string? Save()
    {
        if (_order is null || _store() is not { } store) return null;
        if (Blocked() is { } blocked) return blocked;
        try
        {
            store.Orders.Save(_order);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private void UpdateListRow()
    {
        if (_order is null) return;
        var row = RowOf(_order);
        int at = _all.FindIndex(r => r.Id == row.Id);
        if (at >= 0) _all[at] = row;
        int index = Orders.ToList().FindIndex(o => o.Id == row.Id);
        if (index < 0) return;
        _replacingRow = true;
        try
        {
            Orders[index] = row;
            _selected = row;
        }
        finally
        {
            _replacingRow = false;
        }
        OnPropertyChanged(nameof(Selected));
    }

    // ── Text ────────────────────────────────────────────────────────

    private static string Day(DateTime date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));

    private static bool TryMoney(string text, out decimal amount)
        => decimal.TryParse(text?.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    private void Report(string? error)
    {
        if (error is not null) Show(error, true);
    }

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }
}
