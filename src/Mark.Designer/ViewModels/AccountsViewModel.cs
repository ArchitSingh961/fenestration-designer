using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Accounts;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Orders;
using Mark.Data;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>The company as printed on invoices and receipts.</summary>
public sealed record CompanyPaper(string Name, IReadOnlyList<string> Lines, string Gstin, byte[]? Logo, IReadOnlyList<string> BankLines);

/// <summary>A saved invoice as a row.</summary>
public sealed record InvoiceRow(Invoice Invoice, string Date, string Taxable, string Tax, string Total, string Status)
{
    public string Number => Invoice.Number;
    public string Client => Invoice.ClientName;
    public string Order => Invoice.OrderNumber;
    public override string ToString() => $"{Number} {Client}";
}

/// <summary>A line of the invoice being made: quantity, HSN and rate can be changed.</summary>
public sealed class DraftLineRow : ViewModelBase
{
    private readonly Action _changed;

    public DraftLineRow(InvoiceLine line, double left, Action changed)
    {
        Line = line;
        Left = left;
        _changed = changed;
        _quantityText = Fmt(line.Quantity);
        _rateText = line.Rate.ToString("0.##", CultureInfo.InvariantCulture);
        _hsn = line.Hsn;
    }

    public InvoiceLine Line { get; }
    public string Description => Line.Description;
    public string Unit => Line.Unit;

    /// <summary>Still to invoice of this line (the order's quantity less earlier invoices).</summary>
    public double Left { get; }
    public string LeftText => $"of {Fmt(Left)} left";

    private string _quantityText;
    public string QuantityText { get => _quantityText; set { if (SetProperty(ref _quantityText, value)) _changed(); } }

    private string _rateText;
    public string RateText { get => _rateText; set { if (SetProperty(ref _rateText, value)) _changed(); } }

    private string _hsn;
    public string Hsn { get => _hsn; set { if (SetProperty(ref _hsn, value)) _changed(); } }

    private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public override string ToString() => Description;
}

/// <summary>A payment of an order as a row of Accounts › Receipts.</summary>
public sealed record ReceiptRow(CustomerOrder Order, OrderPayment Payment, string Date, string Amount, string Mode, ICommand PdfCommand)
{
    public string Client => Order.ClientName;
    public string OrderNumber => Order.OrderNumber;
    public string Number => Payment.ReceiptNumber;
    public string Reference => Payment.Reference;
    public string Kind => CustomerOrder.KindName(Payment.Kind);
    public override string ToString() => $"{Date} {Client} {Amount}";
}

/// <summary>What one client owes, as a row of Accounts › Outstanding.</summary>
public sealed record OutstandingRow(string Client, int Orders, string OrderValue, string Invoiced, string Received, string Due, string Balance,
    string Oldest, decimal DueAmount)
{
    public bool IsDue => DueAmount > 0;
    public override string ToString() => $"{Client} {Due}";
}

/// <summary>An order that can be invoiced (choice for a new invoice).</summary>
public sealed record InvoiceOrderChoice(CustomerOrder Order)
{
    public override string ToString() => $"{Order.OrderNumber} · {Order.ClientName} · {Order.ProjectName}";
}

/// <summary>A GST state, for the place of supply and the company's state.</summary>
public sealed record StateChoice(string Code, string Name)
{
    public override string ToString() => $"{Name} ({Code})";
}

/// <summary>
/// Milestone 19, accounts. Invoices: GST tax invoices from orders — every design (and the charges) not invoiced yet, at
/// the order's prices, CGST + SGST within the company's state or IGST otherwise, numbered by financial year — with a PDF;
/// cancelling keeps the number. Receipts: every payment of every order, with a receipt PDF (RCPT-00001…). Outstanding:
/// what each client owes on invoices and on orders. Export and setup: registers for Excel and a Tally import file for a
/// period, and the invoice numbering, HSN codes and Tally ledger names. MARK keeps no books itself.
/// </summary>
public sealed class AccountsViewModel : ViewModelBase
{
    private static readonly CultureInfo Indian = CultureInfo.GetCultureInfo("en-IN");

    private readonly Func<LocalStore?> _store;
    private readonly Func<IProductLibrary> _library;
    private readonly Func<CalculationRules> _rules;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<CompanyPaper> _company;
    private readonly Func<string> _user;
    private List<Invoice> _invoices = new();
    private List<CustomerOrder> _orders = new();
    private AccountsSettings _settings = new();
    private Invoice? _draft;

    public AccountsViewModel(Func<LocalStore?> store, Func<IProductLibrary> library, Func<CalculationRules> rules,
        Func<IDialogService?> dialogs, Func<CompanyPaper> company, Func<string> user)
    {
        _store = store;
        _library = library;
        _rules = rules;
        _dialogs = dialogs;
        _company = company;
        _user = user;
        DraftCommand = new RelayCommand(() => Report(MakeDraft()));
        CreateInvoiceCommand = new RelayCommand(() => Report(CreateInvoice(null)));
        DiscardDraftCommand = new RelayCommand(DiscardDraft);
        InvoicePdfCommand = new RelayCommand(() => Report(InvoicePdf(null)));
        CancelInvoiceCommand = new RelayCommand(() => Report(CancelInvoice()));
        ExportCommand = new RelayCommand(p => Report(Export(p as string ?? "", null)));
        SaveSettingsCommand = new RelayCommand(() => Report(SaveSettings()));
    }

    public Func<string?> Blocked { get; set; } = () => null;
    public Action<string>? OpenDocument { get; set; }

    private string? _message;
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    private void Report(string? error)
    {
        if (error is not null) Show(error, true);
    }

    private void Show(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }

    public static IReadOnlyList<StateChoice> States { get; } =
        Gst.States.Select(s => new StateChoice(s.Key, s.Value)).OrderBy(s => s.Name).ToList();

    // ── Loading ─────────────────────────────────────────────────────

    public void Reload()
    {
        Message = null;
        LoadAll();
        LoadSettingsForm();
    }

    private void LoadAll()
    {
        if (_store() is not { } store)
        {
            Show("There is no local database.", true);
            return;
        }
        try
        {
            _settings = store.Settings.LoadAccountsSettings();
            _invoices = store.Invoices.List().ToList();
            _orders = store.Orders.List().ToList();
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
            return;
        }
        ShowInvoices();
        ShowReceipts();
        ShowOutstanding();
        var chosen = _orderToInvoice?.Order.Id;
        OrderChoices = _orders.OrderByDescending(o => o.OrderNumber).Select(o => new InvoiceOrderChoice(o)).ToList();
        _orderToInvoice = OrderChoices.FirstOrDefault(c => c.Order.Id == chosen) ?? OrderChoices.FirstOrDefault();
        OnPropertyChanged(nameof(OrderChoices));
        OnPropertyChanged(nameof(OrderToInvoice));
    }

    // ── Invoices ────────────────────────────────────────────────────

    public ObservableCollection<InvoiceRow> Invoices { get; } = new();
    public ObservableCollection<DraftLineRow> DraftLines { get; } = new();
    public IReadOnlyList<InvoiceOrderChoice> OrderChoices { get; private set; } = Array.Empty<InvoiceOrderChoice>();

    private InvoiceOrderChoice? _orderToInvoice;
    public InvoiceOrderChoice? OrderToInvoice { get => _orderToInvoice; set => SetProperty(ref _orderToInvoice, value); }

    private string _invoiceSearch = "";
    public string InvoiceSearch { get => _invoiceSearch; set { if (SetProperty(ref _invoiceSearch, value)) ShowInvoices(); } }

    private InvoiceRow? _selectedInvoice;
    public InvoiceRow? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (!SetProperty(ref _selectedInvoice, value)) return;
            OnPropertyChanged(nameof(HasSelectedInvoice));
            OnPropertyChanged(nameof(InvoiceDetail));
        }
    }

    public bool HasSelectedInvoice => _selectedInvoice is not null && !HasDraft;

    /// <summary>The selected invoice in a few lines.</summary>
    public string InvoiceDetail => _selectedInvoice?.Invoice is not { } i ? ""
        : string.Join("\n", new[]
        {
            $"{i.Number} · {i.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} · order {i.OrderNumber}{(i.IsCancelled ? " · CANCELLED" : "")}",
            $"{i.ClientName}{(i.ClientGstin.Length > 0 ? $" · GSTIN {i.ClientGstin}" : " · consumer (B2C)")}",
            $"Place of supply {Gst.StateText(i.PlaceOfSupply)} · {(i.IsIntraState ? "CGST + SGST" : "IGST")} {i.TaxPercent:0.##} %",
            $"{i.Lines.Count} line{(i.Lines.Count == 1 ? "" : "s")} · taxable {Money(i.Taxable)} · tax {Money(i.Tax)} · total {Money(i.Total)}",
            i.IsCancelled && i.CancelledReason.Length > 0 ? $"Cancelled: {i.CancelledReason}" : ""
        }.Where(l => l.Length > 0));

    public bool HasDraft => _draft is not null;
    public string DraftTitle => _draft is null ? "" : $"New invoice · order {_draft.OrderNumber} · {_draft.ClientName}";

    private string _draftDate = "";
    public string DraftDate { get => _draftDate; set { if (SetProperty(ref _draftDate, value)) DraftChanged(); } }

    private StateChoice? _placeOfSupply;
    public StateChoice? PlaceOfSupply { get => _placeOfSupply; set { if (SetProperty(ref _placeOfSupply, value)) DraftChanged(); } }

    private string _draftNotes = "";
    public string DraftNotes { get => _draftNotes; set => SetProperty(ref _draftNotes, value); }

    private string _draftWarning = "";
    /// <summary>What to check before making the invoice (e.g. the company's state is not known).</summary>
    public string DraftWarning { get => _draftWarning; private set => SetProperty(ref _draftWarning, value); }

    /// <summary>The draft's totals: taxable, CGST/SGST or IGST, round off, total.</summary>
    public string DraftTotals { get => _draftTotals; private set => SetProperty(ref _draftTotals, value); }
    private string _draftTotals = "";

    private decimal _priceFactor = 1;
    private List<string> _warnings = new();

    /// <summary>Today's prices differ from the value the order was confirmed at.</summary>
    public bool HasPriceDifference => _draft is not null && _priceFactor != 1;

    /// <summary>Scales the designs' rates so the order comes to the value it was confirmed at (the agreed price); charges stay.</summary>
    public ICommand MatchOrderValueCommand => _matchOrderValue ??= new RelayCommand(MatchOrderValue);
    private ICommand? _matchOrderValue;

    public void MatchOrderValue()
    {
        if (_draft is null || _priceFactor == 1) return;
        foreach (var row in DraftLines.Where(r => r.Line.Key.StartsWith("frame:", StringComparison.Ordinal)))
            if (PricingViewModel.TryParseDecimal(row.RateText, out decimal rate))
                row.RateText = Math.Round(rate * _priceFactor, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
        _priceFactor = 1;
        _warnings.RemoveAll(w => w.Contains("confirmed at"));
        DraftWarning = string.Join(" ", _warnings);
        OnPropertyChanged(nameof(HasPriceDifference));
        Show("The rates now come to the order's confirmed value.", false);
    }

    public ICommand DraftCommand { get; }
    public ICommand CreateInvoiceCommand { get; }
    public ICommand DiscardDraftCommand { get; }
    public ICommand InvoicePdfCommand { get; }
    public ICommand CancelInvoiceCommand { get; }

    private void ShowInvoices()
    {
        var selected = _selectedInvoice?.Invoice.Id;
        Invoices.Clear();
        string search = (_invoiceSearch ?? "").Trim();
        foreach (var i in _invoices.Where(i => search.Length == 0 || i.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                || i.ClientName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                || i.OrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Invoices.Add(new InvoiceRow(i, i.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), Money(i.Taxable), Money(i.Tax),
                Money(i.Total), i.IsCancelled ? "Cancelled" : i.IsB2B ? "B2B" : "B2C"));
        _selectedInvoice = Invoices.FirstOrDefault(r => r.Invoice.Id == selected);
        OnPropertyChanged(nameof(SelectedInvoice));
        OnPropertyChanged(nameof(HasSelectedInvoice));
        OnPropertyChanged(nameof(InvoiceDetail));
    }

    /// <summary>
    /// A draft invoice for <see cref="OrderToInvoice"/>: every design not invoiced yet (at the order's unit price after
    /// discount, before tax) and the charges once. Returns an error, or null.
    /// </summary>
    public string? MakeDraft()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_orderToInvoice?.Order is not { } order) return "Choose the order to invoice.";
        Project project;
        try
        {
            project = _store()!.Projects.Load(order.ProjectId);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        var library = _library();
        var rules = _rules();
        var result = new CalculationEngine().Calculate(project, library, rules);
        var price = PricingEngine.Price(project, result, project.Pricing, rules.MoneyDecimals);
        var earlier = _invoices.Where(i => i.OrderId == order.Id && !i.IsCancelled).ToList();
        double Invoiced(string key) => earlier.Sum(i => i.QuantityOf(key));

        var lines = new List<(InvoiceLine Line, double Left)>();
        for (int n = 0; n < project.Frames.Count; n++)
        {
            var frame = project.Frames[n];
            if (price.FindDesign(frame.Id) is not { } design) continue;
            string key = $"frame:{frame.Id:N}";
            double left = design.Quantity - Invoiced(key);
            if (left <= 0) continue;
            var system = library.FindSystem(frame.SystemId);
            string reference = string.IsNullOrWhiteSpace(frame.Design.Reference) ? $"W{n + 1}" : frame.Design.Reference;
            string description = string.Join(" · ", new[]
            {
                reference, frame.Design.Name, $"{frame.Width:0} × {frame.Height:0} mm", system?.Name ?? "", frame.Design.Location
            }.Where(t => !string.IsNullOrWhiteSpace(t)));
            string hsn = system?.Material == SystemMaterial.Upvc ? _settings.HsnUpvc : _settings.HsnAluminium;
            lines.Add((new InvoiceLine(key, description, hsn, left, "Nos", design.UnitPrice), left));
        }
        foreach (var charge in price.Summary.Where(l => l.Kind == PriceSummaryKind.Charge && l.Amount != 0))
        {
            string key = $"charge:{charge.Name}";
            if (Invoiced(key) > 0) continue;
            lines.Add((new InvoiceLine(key, charge.Name, _settings.SacCharges, 1, "Job", charge.Amount), 1));
        }
        if (lines.Count == 0) return $"Everything of {order.OrderNumber} has been invoiced.";

        var client = project.Quote.Client;
        string sellerState = Gst.StateCodeOfGstin(_company().Gstin) ?? (_settings.CompanyState.Length > 0 ? _settings.CompanyState : "");
        string place = Gst.StateCodeOfGstin(client.Gstin) ?? Gst.StateCodeOfName(client.State) ?? sellerState;
        _draft = new Invoice
        {
            Date = DateTime.Today,
            ProjectId = project.Id,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            QuoteNumber = project.Quote.NumberText,
            ProjectName = project.Name,
            ClientName = client.Company.Trim().Length > 0 ? client.Company.Trim() : client.DisplayName,
            ClientAddress = client.AddressText,
            ClientGstin = client.Gstin,
            ClientPhone = client.Phone,
            SiteAddress = order.SiteAddress,
            SellerState = sellerState,
            PlaceOfSupply = place,
            TaxPercent = project.Pricing.TaxPercent,
            CreatedBy = _user()
        };
        DraftLines.Clear();
        foreach (var (line, left) in lines) DraftLines.Add(new DraftLineRow(line, left, DraftChanged));
        _draftDate = _draft.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        _placeOfSupply = States.FirstOrDefault(s => s.Code == place);
        _draftNotes = "";
        OnPropertyChanged(nameof(DraftDate));
        OnPropertyChanged(nameof(PlaceOfSupply));
        OnPropertyChanged(nameof(DraftNotes));

        var warnings = _warnings = new List<string>();
        if (sellerState.Length == 0)
            warnings.Add("Your company's state is not known (no GSTIN in Sales › Company & quotation, no state in Export and setup), so the tax is IGST.");
        if (place.Length == 0) warnings.Add("Choose the place of supply (the client's state).");
        _priceFactor = 1;
        if (order.Value is { } value && price.GrandTotal > 0 && Math.Abs(value - price.GrandTotal) >= 1)
        {
            warnings.Add($"The order was confirmed at {Money(value)}; at today's prices it comes to {Money(price.GrandTotal)}. Check the rates, or use the order's value.");
            // Only the designs' prices changed: the charges stay as agreed. The designs are scaled so that
            // (designs + charges) × (1 + tax) comes to the confirmed value.
            decimal charges = lines.Where(l => l.Line.Key.StartsWith("charge:", StringComparison.Ordinal)).Sum(l => l.Line.Taxable);
            decimal designs = lines.Where(l => l.Line.Key.StartsWith("frame:", StringComparison.Ordinal)).Sum(l => l.Line.Taxable);
            decimal target = value / (1 + project.Pricing.TaxPercent / 100m) - charges;
            _priceFactor = earlier.Count == 0 && designs > 0 && target > 0 ? target / designs : value / price.GrandTotal;
        }
        OnPropertyChanged(nameof(HasPriceDifference));
        DraftWarning = string.Join(" ", warnings);
        DraftChanged();
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(DraftTitle));
        OnPropertyChanged(nameof(HasSelectedInvoice));
        Message = null;
        return null;
    }

    /// <summary>Reads the draft form into the draft invoice; returns an error, or null.</summary>
    private string? TakeDraft()
    {
        if (_draft is null) return "Make the invoice from an order first.";
        if (!DateTime.TryParseExact((_draftDate ?? "").Trim(), new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return "Enter the invoice date as dd-mm-yyyy.";
        _draft.Date = date;
        _draft.PlaceOfSupply = _placeOfSupply?.Code ?? "";
        _draft.Notes = (_draftNotes ?? "").Trim();
        var lines = new List<InvoiceLine>();
        foreach (var row in DraftLines)
        {
            if (!double.TryParse(row.QuantityText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double quantity) || quantity < 0)
                return $"Enter the quantity of {row.Description} (0 to leave it out).";
            if (quantity > row.Left + 1e-9) return $"Only {row.Left:0.##} of {row.Description} is left to invoice.";
            if (!PricingViewModel.TryParseDecimal(row.RateText, out decimal rate) || rate < 0) return $"Enter the rate of {row.Description}.";
            if ((row.Hsn ?? "").Trim().Length > 8) return "An HSN/SAC code has at most 8 digits.";
            if (quantity > 0) lines.Add(row.Line with { Quantity = quantity, Rate = rate, Hsn = (row.Hsn ?? "").Trim() });
        }
        if (lines.Count == 0) return "Invoice at least one line.";
        _draft.Lines = lines;
        return null;
    }

    private void DraftChanged()
    {
        if (_draft is null) return;
        if (TakeDraft() is { } error)
        {
            DraftTotals = error;
            return;
        }
        var i = _draft;
        DraftTotals = i.IsIntraState
            ? $"Taxable {Money(i.Taxable)}  ·  CGST {i.TaxPercent / 2:0.##} % {Money(i.Cgst)}  ·  SGST {i.TaxPercent / 2:0.##} % {Money(i.Sgst)}  ·  round off {Money(i.RoundOff)}  ·  Total {Money(i.Total)}"
            : $"Taxable {Money(i.Taxable)}  ·  IGST {i.TaxPercent:0.##} % {Money(i.Igst)}  ·  round off {Money(i.RoundOff)}  ·  Total {Money(i.Total)}";
    }

    /// <summary>Numbers and saves the draft, then writes its PDF (to <paramref name="path"/>, or where the user chooses).</summary>
    public string? CreateInvoice(string? path)
    {
        if (Blocked() is { } blocked) return blocked;
        if (TakeDraft() is { } error) return error;
        var invoice = _draft!;
        if (invoice.PlaceOfSupply.Length == 0) return "Choose the place of supply (the client's state).";
        try
        {
            _store()!.Invoices.Save(invoice, _settings);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        _draft = null;
        DraftLines.Clear();
        DraftWarning = "";
        OnPropertyChanged(nameof(HasDraft));
        LoadAll();
        SelectedInvoice = Invoices.FirstOrDefault(r => r.Invoice.Id == invoice.Id);
        Show($"Made invoice {invoice.Number} for {Money(invoice.Total)}.", false);
        return InvoicePdf(path);
    }

    private void DiscardDraft()
    {
        _draft = null;
        DraftLines.Clear();
        DraftWarning = "";
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(HasSelectedInvoice));
    }

    /// <summary>Writes the selected invoice as a PDF and opens it.</summary>
    public string? InvoicePdf(string? path)
    {
        if (_selectedInvoice?.Invoice is not { } i) return "Choose an invoice.";
        path ??= _dialogs()?.ChooseSaveFile("Save invoice", "PDF files (*.pdf)|*.pdf", FileName($"Invoice {i.Number} {i.ClientName}") + ".pdf");
        if (path is null) return null;
        var company = _company();
        var totals = new List<(string, string, bool)> { ("Taxable value", Money(i.Taxable), false) };
        if (i.IsIntraState)
        {
            totals.Add(($"CGST @ {i.TaxPercent / 2:0.##}%", Money(i.Cgst), false));
            totals.Add(($"SGST @ {i.TaxPercent / 2:0.##}%", Money(i.Sgst), false));
        }
        else totals.Add(($"IGST @ {i.TaxPercent:0.##}%", Money(i.Igst), false));
        if (i.RoundOff != 0) totals.Add(("Round off", Money(i.RoundOff), false));
        totals.Add(("Invoice total", Money(i.Total), true));
        var paper = new InvoicePaper
        {
            CompanyName = company.Name,
            CompanyLines = company.Lines,
            Logo = company.Logo,
            Number = i.Number,
            Date = i.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            OrderNumber = i.OrderNumber,
            PlaceOfSupply = Gst.StateText(i.PlaceOfSupply),
            BuyerName = i.ClientName,
            BuyerLines = new[] { i.ClientAddress, i.ClientPhone.Length > 0 ? $"Phone: {i.ClientPhone}" : "",
                i.ClientGstin.Length > 0 ? $"GSTIN: {i.ClientGstin}" : "Unregistered (consumer)" },
            ShipTo = i.SiteAddress,
            Lines = i.Lines.Select(l => new InvoicePaperLine(l.Description, l.Hsn, l.Quantity.ToString("0.##", CultureInfo.InvariantCulture), l.Unit,
                Money(l.Rate), Money(l.Taxable))).ToList(),
            Totals = totals,
            AmountInWords = Gst.AmountInWords(i.Total),
            BankLines = company.BankLines,
            Notes = i.Notes,
            IsCancelled = i.IsCancelled
        };
        try
        {
            using (var stream = File.Create(path))
                AccountsPdf.WriteInvoice(paper, stream);
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The invoice could not be written: {ex.Message}";
        }
    }

    /// <summary>Cancels the selected invoice (its number is kept and not used again); what it had can be invoiced again.</summary>
    public string? CancelInvoice()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_selectedInvoice?.Invoice is not { } i) return "Choose an invoice.";
        if (i.IsCancelled) return $"{i.Number} is already cancelled.";
        string? reason = _dialogs() is { } dialogs ? dialogs.PromptText("Cancel invoice", $"Why is {i.Number} cancelled?", "") : "Cancelled";
        if (reason is null) return null;
        i.IsCancelled = true;
        i.CancelledReason = reason.Trim();
        try
        {
            _store()!.Invoices.Save(i, _settings);
        }
        catch (DataStoreException ex)
        {
            i.IsCancelled = false;
            return ex.Message;
        }
        LoadAll();
        Show($"Cancelled {i.Number}. Its number stays used; what it had can be invoiced again.", false);
        return null;
    }

    // ── Receipts ────────────────────────────────────────────────────

    public ObservableCollection<ReceiptRow> Receipts { get; } = new();

    public string ReceiptsTotal => $"{Receipts.Count} payment{(Receipts.Count == 1 ? "" : "s")} · {Money(Receipts.Sum(r => r.Payment.Amount))}";

    private void ShowReceipts()
    {
        Receipts.Clear();
        foreach (var (order, payment) in _orders.SelectMany(o => o.Payments.Select(p => (o, p))).OrderByDescending(x => x.p.Date))
        {
            var o = order;
            var id = payment.Id;
            Receipts.Add(new ReceiptRow(o, payment, payment.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture), Money(payment.Amount),
                CustomerOrder.MethodName(payment.Method), new RelayCommand(() => Report(ReceiptPdf(o.Id, id, null)))));
        }
        OnPropertyChanged(nameof(ReceiptsTotal));
    }

    /// <summary>Writes a payment's receipt (giving it the next number, RCPT-00001…, the first time) and opens it.</summary>
    public string? ReceiptPdf(Guid orderId, Guid paymentId, string? path)
    {
        if (Blocked() is { } blocked) return blocked;
        var store = _store()!;
        CustomerOrder order;
        try
        {
            order = store.Orders.Load(orderId);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        int index = order.Payments.FindIndex(p => p.Id == paymentId);
        if (index < 0) return "That payment is no longer on the order.";
        var payment = order.Payments[index];
        if (payment.ReceiptNumber.Length == 0)
        {
            string number = NextReceiptNumber();
            payment = payment with { ReceiptNumber = number };
            order.Payments[index] = payment;
            try
            {
                store.Orders.Save(order);
            }
            catch (DataStoreException ex)
            {
                return ex.Message;
            }
        }
        path ??= _dialogs()?.ChooseSaveFile("Save receipt", "PDF files (*.pdf)|*.pdf", FileName($"Receipt {payment.ReceiptNumber} {order.ClientName}") + ".pdf");
        if (path is null)
        {
            LoadAll();
            return null;
        }
        var company = _company();
        decimal paidUpTo = order.Payments.Where(p => p.Date <= payment.Date).Sum(p => p.Amount);
        var paper = new ReceiptPaper
        {
            CompanyName = company.Name,
            CompanyLines = company.Lines,
            Logo = company.Logo,
            Number = payment.ReceiptNumber,
            Date = payment.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            ReceivedFrom = order.ClientName,
            Amount = Money(payment.Amount),
            AmountInWords = Gst.AmountInWords(payment.Amount),
            Kind = CustomerOrder.KindName(payment.Kind),
            Mode = CustomerOrder.MethodName(payment.Method),
            Reference = payment.Reference,
            Against = $"Order {order.OrderNumber} · {order.ProjectName}".Trim(' ', '·'),
            OrderValue = order.Value is { } v ? Money(v) : "",
            ReceivedSoFar = Money(paidUpTo),
            Balance = order.Value is { } value ? Money(Math.Max(0, value - paidUpTo)) : "",
            Note = payment.Note
        };
        try
        {
            using (var stream = File.Create(path))
                AccountsPdf.WriteReceipt(paper, stream);
            OpenDocument?.Invoke(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The receipt could not be written: {ex.Message}";
        }
        LoadAll();
        Show($"Saved receipt {payment.ReceiptNumber}.", false);
        return null;
    }

    private string NextReceiptNumber()
    {
        int highest = _store()!.Orders.List().SelectMany(o => o.Payments).Select(p => p.ReceiptNumber)
            .Select(n => n.StartsWith("RCPT-", StringComparison.Ordinal) && int.TryParse(n[5..], out int x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"RCPT-{(highest + 1).ToString("D5", CultureInfo.InvariantCulture)}";
    }

    // ── Outstanding ─────────────────────────────────────────────────

    public ObservableCollection<OutstandingRow> Outstanding { get; } = new();

    public string OutstandingTotal { get; private set; } = "";

    private void ShowOutstanding()
    {
        Outstanding.Clear();
        var live = _invoices.Where(i => !i.IsCancelled).ToList();
        decimal totalDue = 0, totalBalance = 0;
        foreach (var client in _orders.GroupBy(o => o.ClientName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var ids = client.Select(o => o.Id).ToHashSet();
            var invoices = live.Where(i => ids.Contains(i.OrderId)).ToList();
            decimal value = client.Sum(o => o.Value ?? 0), invoiced = invoices.Sum(i => i.Total), received = client.Sum(o => o.Paid);
            decimal due = Math.Max(0, invoiced - received), balance = Math.Max(0, value - received);
            totalDue += due;
            totalBalance += balance;
            string oldest = "";
            if (due > 0 && invoices.Count > 0)
            {
                // The oldest invoice not covered by what was received (payments go to the oldest invoices first).
                decimal left = received;
                foreach (var i in invoices.OrderBy(i => i.Date))
                {
                    if (left >= i.Total) { left -= i.Total; continue; }
                    oldest = $"{i.Number} · {(DateTime.Today - i.Date.Date).Days} days";
                    break;
                }
            }
            Outstanding.Add(new OutstandingRow(client.Key.Length == 0 ? "(no client)" : client.Key, client.Count(), Money(value), Money(invoiced),
                Money(received), Money(due), Money(balance), oldest, due));
        }
        var sorted = Outstanding.OrderByDescending(r => r.DueAmount).ThenBy(r => r.Client).ToList();
        Outstanding.Clear();
        foreach (var row in sorted) Outstanding.Add(row);
        OutstandingTotal = $"Due on invoices {Money(totalDue)}  ·  still to receive on orders {Money(totalBalance)}";
        OnPropertyChanged(nameof(OutstandingTotal));
    }

    // ── Export and setup ────────────────────────────────────────────

    private string _fromText = "";
    public string FromText { get => _fromText; set => SetProperty(ref _fromText, value); }

    private string _toText = "";
    public string ToText { get => _toText; set => SetProperty(ref _toText, value); }

    public ICommand ExportCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    /// <summary>
    /// Writes an export of the period: "invoices", "hsn", "receipts" (CSV, opens in Excel) or "tally" (XML).
    /// Returns an error, or null.
    /// </summary>
    public string? Export(string what, string? path)
    {
        if (!TryDate(FromText, out var from) || !TryDate(ToText, out var to)) return "Enter the period as dd-mm-yyyy to dd-mm-yyyy.";
        if (to < from) return "The period ends before it starts.";
        var invoices = _invoices.Where(i => i.Date.Date >= from && i.Date.Date <= to).ToList();
        var receipts = _orders.SelectMany(o => o.Payments.Where(p => p.Date.Date >= from && p.Date.Date <= to).Select(p =>
            new ReceiptEntry(p.Date, p.ReceiptNumber.Length > 0 ? p.ReceiptNumber : $"{o.OrderNumber}/{p.Date:ddMMyy}", o.ClientName, o.OrderNumber,
                p.Amount, CustomerOrder.MethodName(p.Method), p.Reference, p.Method == PaymentMethod.Cash))).ToList();
        string period = $"{from:dd-MM-yyyy} to {to:dd-MM-yyyy}";
        (string Text, string Name, string Filter) file = what switch
        {
            "invoices" => (AccountsExport.InvoicesCsv(invoices), $"Invoices {period}.csv", "Excel CSV (*.csv)|*.csv"),
            "hsn" => (AccountsExport.HsnSummaryCsv(invoices), $"HSN summary {period}.csv", "Excel CSV (*.csv)|*.csv"),
            "receipts" => (AccountsExport.ReceiptsCsv(receipts), $"Receipts {period}.csv", "Excel CSV (*.csv)|*.csv"),
            "tally" => (AccountsExport.TallyXml(invoices, receipts, _settings), $"Tally {period}.xml", "Tally XML (*.xml)|*.xml"),
            _ => ("", "", "")
        };
        if (file.Name.Length == 0) return "Choose what to export.";
        path ??= _dialogs()?.ChooseSaveFile("Export", file.Filter, file.Name);
        if (path is null) return null;
        try
        {
            // Excel reads UTF-8 (₹, names in Hindi) only with the byte order mark.
            File.WriteAllText(path, file.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: what != "tally"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The export could not be written: {ex.Message}";
        }
        int count = what is "receipts" ? receipts.Count : invoices.Count;
        Show(what == "tally"
            ? $"Saved {Path.GetFileName(path)}: {invoices.Count(i => !i.IsCancelled)} sales and {receipts.Count} receipt vouchers. In Tally: Gateway › Import › Transactions."
            : $"Saved {Path.GetFileName(path)} ({count} {(what == "receipts" ? "payments" : "invoices")}).", false);
        return null;
    }

    // Setup form.
    private string _prefix = "";
    public string Prefix { get => _prefix; set => SetProperty(ref _prefix, value); }
    private bool _byYear;
    public bool ByYear { get => _byYear; set => SetProperty(ref _byYear, value); }
    private string _hsnAluminium = "";
    public string HsnAluminium { get => _hsnAluminium; set => SetProperty(ref _hsnAluminium, value); }
    private string _hsnUpvc = "";
    public string HsnUpvc { get => _hsnUpvc; set => SetProperty(ref _hsnUpvc, value); }
    private string _sacCharges = "";
    public string SacCharges { get => _sacCharges; set => SetProperty(ref _sacCharges, value); }
    private StateChoice? _companyState;
    public StateChoice? CompanyState { get => _companyState; set => SetProperty(ref _companyState, value); }
    private string _salesLedger = "";
    public string SalesLedger { get => _salesLedger; set => SetProperty(ref _salesLedger, value); }
    private string _cgstLedger = "";
    public string CgstLedger { get => _cgstLedger; set => SetProperty(ref _cgstLedger, value); }
    private string _sgstLedger = "";
    public string SgstLedger { get => _sgstLedger; set => SetProperty(ref _sgstLedger, value); }
    private string _igstLedger = "";
    public string IgstLedger { get => _igstLedger; set => SetProperty(ref _igstLedger, value); }
    private string _roundOffLedger = "";
    public string RoundOffLedger { get => _roundOffLedger; set => SetProperty(ref _roundOffLedger, value); }
    private string _cashLedger = "";
    public string CashLedger { get => _cashLedger; set => SetProperty(ref _cashLedger, value); }
    private string _bankLedger = "";
    public string BankLedger { get => _bankLedger; set => SetProperty(ref _bankLedger, value); }
    private string _debtorsGroup = "";
    public string DebtorsGroup { get => _debtorsGroup; set => SetProperty(ref _debtorsGroup, value); }
    private string _tallyCompany = "";
    public string TallyCompany { get => _tallyCompany; set => SetProperty(ref _tallyCompany, value); }

    /// <summary>"Next invoice: INV/2026-27/0004".</summary>
    public string NextInvoiceText => $"Next invoice: {Invoice.NextNumber(_settings.InvoicePrefix, _settings.NumberByFinancialYear, DateTime.Today, _invoices.Select(i => i.Number))}";

    private void LoadSettingsForm()
    {
        var s = _settings;
        _prefix = s.InvoicePrefix; _byYear = s.NumberByFinancialYear; _hsnAluminium = s.HsnAluminium; _hsnUpvc = s.HsnUpvc;
        _sacCharges = s.SacCharges; _companyState = States.FirstOrDefault(x => x.Code == s.CompanyState);
        _salesLedger = s.SalesLedger; _cgstLedger = s.CgstLedger; _sgstLedger = s.SgstLedger; _igstLedger = s.IgstLedger;
        _roundOffLedger = s.RoundOffLedger; _cashLedger = s.CashLedger; _bankLedger = s.BankLedger; _debtorsGroup = s.DebtorsGroup;
        _tallyCompany = s.TallyCompany;
        var today = DateTime.Today;
        var yearStart = new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
        if (_fromText.Length == 0) _fromText = yearStart.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        if (_toText.Length == 0) _toText = today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        OnPropertyChanged(string.Empty);
    }

    public string? SaveSettings()
    {
        if (Blocked() is { } blocked) return blocked;
        string prefix = (Prefix ?? "").Trim();
        if (prefix.Length is 0 or > 10 || prefix.Any(c => !char.IsLetterOrDigit(c)))
            return "The invoice prefix is 1 to 10 letters or digits, e.g. INV.";
        foreach (var (label, code) in new[] { ("aluminium HSN", HsnAluminium), ("uPVC HSN", HsnUpvc), ("charges HSN/SAC", SacCharges) })
            if ((code ?? "").Trim().Length > 8 || (code ?? "").Trim().Any(c => !char.IsDigit(c))) return $"The {label} is up to 8 digits.";
        var ledgers = new[] { SalesLedger, CgstLedger, SgstLedger, IgstLedger, RoundOffLedger, CashLedger, BankLedger, DebtorsGroup };
        if (ledgers.Any(l => string.IsNullOrWhiteSpace(l))) return "Give every Tally ledger a name (as it is in Tally).";
        _settings = new AccountsSettings
        {
            InvoicePrefix = prefix, NumberByFinancialYear = ByYear, HsnAluminium = HsnAluminium.Trim(), HsnUpvc = HsnUpvc.Trim(),
            SacCharges = SacCharges.Trim(), CompanyState = CompanyState?.Code ?? "", SalesLedger = SalesLedger.Trim(),
            CgstLedger = CgstLedger.Trim(), SgstLedger = SgstLedger.Trim(), IgstLedger = IgstLedger.Trim(), RoundOffLedger = RoundOffLedger.Trim(),
            CashLedger = CashLedger.Trim(), BankLedger = BankLedger.Trim(), DebtorsGroup = DebtorsGroup.Trim(), TallyCompany = (TallyCompany ?? "").Trim()
        };
        try
        {
            _store()!.Settings.SaveAccountsSettings(_settings);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        OnPropertyChanged(nameof(NextInvoiceText));
        Show("Saved the accounts setup.", false);
        return null;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string Money(decimal value) => $"₹ {value.ToString("N2", Indian)}";

    private static bool TryDate(string? text, out DateTime date)
        => DateTime.TryParseExact((text ?? "").Trim(), new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string FileName(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
}
