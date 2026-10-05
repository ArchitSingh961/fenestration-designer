using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Inventory;
using Mark.Core.Library;
using Mark.Core.Production;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>An item's stock position as a row of Inventory › Stock.</summary>
public sealed record StockRow(StockPosition Position, string Kind, string OnHand, string Reserved, string Available, string OnOrder,
    string Reorder, string ToBuy, string ForOrders)
{
    public string Name => Position.Name;
    public string Unit => Position.Unit;
    public bool IsLow => Position.IsLow;
    public string Location => Position.Location;
    public override string ToString() => Name;
}

/// <summary>A stock move as a row (newest first).</summary>
public sealed record StockMoveRow(string When, string Item, string Quantity, string Reason, string Reference, string By, string Note)
{
    public override string ToString() => $"{When} {Item} {Quantity}";
}

/// <summary>A production order whose stock has not been issued yet.</summary>
public sealed record IssueRow(Guid OrderId, string OrderNumber, string Project, string Needs, ICommand IssueCommand)
{
    public override string ToString() => OrderNumber;
}

/// <summary>A purchase order as a row of Purchasing › Purchase orders.</summary>
public sealed record PurchaseRow(PurchaseOrder Order, string Date, string Status, string Total)
{
    public string Number => Order.Number;
    public string Supplier => Order.SupplierName;
    public string ForOrders => Order.ForOrders;
    public override string ToString() => $"{Number} {Supplier}";
}

/// <summary>A line of the open purchase order: editable while it is a draft, received against once ordered.</summary>
public sealed class PurchaseLineRow : ViewModelBase
{
    public PurchaseLineRow(PurchaseLine line, double received, Action<PurchaseLineRow> remove)
    {
        Line = line;
        Received = received;
        _quantityText = InventoryViewModel.Number(line.Quantity);
        _rateText = line.Rate.ToString("0.##", CultureInfo.InvariantCulture);
        _receiveText = InventoryViewModel.Number(Math.Max(0, line.Quantity - received));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public PurchaseLine Line { get; }
    public string Name => Line.Name;
    public string Unit => Line.Unit;
    public double Received { get; }
    public string ReceivedText => InventoryViewModel.Number(Received);

    private string _quantityText;
    public string QuantityText { get => _quantityText; set => SetProperty(ref _quantityText, value); }

    private string _rateText;
    public string RateText { get => _rateText; set => SetProperty(ref _rateText, value); }

    private string _receiveText;
    /// <summary>How much came in now (goods receipt).</summary>
    public string ReceiveText { get => _receiveText; set => SetProperty(ref _receiveText, value); }

    public ICommand RemoveCommand { get; }

    public override string ToString() => Name;
}

/// <summary>A library item that can be stocked or bought, for the item pickers.</summary>
public sealed record StockItemChoice(StockKey Key, string Name, string Unit)
{
    public override string ToString() => $"{Name} ({Unit})";
}

/// <summary>
/// Milestone 18, purchasing and inventory. Inventory › Stock: every item's stock — on hand, reserved for production orders
/// whose stock is not issued yet, available, on order — with low-stock alerts, counting and adjusting, reorder levels,
/// the ledger, and issuing an order's stock to production. Purchasing › Purchase orders: purchase orders worked out from
/// what orders need minus stock and what is on order, the PDF for the supplier, and goods received into stock.
/// Purchasing › Suppliers. Everything is in the local database.
/// </summary>
public sealed class InventoryViewModel : ViewModelBase
{
    private readonly Func<LocalStore?> _store;
    private readonly Func<IProductLibrary> _library;
    private readonly Func<CalculationRules> _rules;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<Letterhead> _letterhead;
    private readonly Func<string> _user;
    private readonly Dictionary<Guid, (string Json, IReadOnlyList<StockNeed> Needs)> _needsCache = new();
    private List<StockPosition> _positions = new();
    private List<PurchaseOrder> _purchases = new();
    private List<(ProductionOrder Order, IReadOnlyList<StockNeed> Needs)> _toIssue = new();

    public InventoryViewModel(Func<LocalStore?> store, Func<IProductLibrary> library, Func<CalculationRules> rules,
        Func<IDialogService?> dialogs, Func<Letterhead> letterhead, Func<string> user)
    {
        _store = store;
        _library = library;
        _rules = rules;
        _dialogs = dialogs;
        _letterhead = letterhead;
        _user = user;
        AdjustCommand = new RelayCommand(() => Report(Adjust()));
        CountCommand = new RelayCommand(() => Report(Count()));
        SaveStockSettingsCommand = new RelayCommand(() => Report(SaveStockSettings()));
        AddStockItemCommand = new RelayCommand(() => Report(AddStockItem()));
        SuggestCommand = new RelayCommand(() => Report(Suggest()));
        NewPurchaseCommand = new RelayCommand(() => Report(NewPurchase()));
        AddLineCommand = new RelayCommand(() => Report(AddLine()));
        SavePurchaseCommand = new RelayCommand(() => Report(SavePurchase()));
        PlaceOrderCommand = new RelayCommand(() => Report(PlaceOrder()));
        CancelPurchaseCommand = new RelayCommand(() => Report(CancelPurchase()));
        ReceiveCommand = new RelayCommand(() => Report(Receive()));
        PurchasePdfCommand = new RelayCommand(() => Report(PurchasePdfFile(null)));
        NewSupplierCommand = new RelayCommand(NewSupplier);
        SaveSupplierCommand = new RelayCommand(() => Report(SaveSupplier()));
    }

    /// <summary>Why nothing can be changed (no database, read-only, not in the package), or null.</summary>
    public Func<string?> Blocked { get; set; } = () => null;

    /// <summary>Opens a written PDF (set by the window).</summary>
    public Action<string>? OpenDocument { get; set; }

    // ── Messages ────────────────────────────────────────────────────

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

    // ── Loading ─────────────────────────────────────────────────────

    /// <summary>Reads stock, purchase orders, suppliers and open production orders again, and works out every position.</summary>
    public void Reload()
    {
        Message = null;
        LoadAll();
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
            var library = _library();
            var issued = store.Inventory.IssuedOrders();
            _toIssue = new();
            foreach (var order in store.Production.List().Where(o => !issued.Contains(o.OrderNumber)))
                _toIssue.Add((order, NeedsOf(store, order, library)));
            _purchases = store.Inventory.PurchaseOrders().ToList();
            _positions = StockNeeds.Positions(store.Inventory.Levels(),
                _toIssue.Select(t => (t.Order.OrderNumber, t.Needs)).ToList(), _purchases, library).ToList();
            LoadSuppliers(store);
            ShowStock();
            ShowIssues();
            ShowPurchases();
            Choices = library.Profiles.Where(p => p.IsActive).Select(p => new StockItemChoice(new StockKey(StockKind.Profile, p.Id), p.Name, "bars"))
                .Concat(library.Glass.Select(g => new StockItemChoice(new StockKey(StockKind.Glass, g.Id), g.Name, "m²")))
                .Concat(library.Materials.Where(m => m.IsActive).Select(m => new StockItemChoice(new StockKey(StockKind.Material, m.Id), m.Name, StockNeeds.UnitOf(m))))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            OnPropertyChanged(nameof(Choices));
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    private IReadOnlyList<StockNeed> NeedsOf(LocalStore store, ProductionOrder order, IProductLibrary library)
    {
        var full = order.DocumentJson.Length > 0 ? order : store.Production.Load(order.Id);
        if (_needsCache.TryGetValue(order.Id, out var cached) && cached.Json == full.DocumentJson) return cached.Needs;
        IReadOnlyList<StockNeed> needs;
        try
        {
            needs = StockNeeds.For(ProjectSerializer.Deserialize(full.DocumentJson), library, _rules());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            needs = Array.Empty<StockNeed>();
        }
        _needsCache[order.Id] = (full.DocumentJson, needs);
        return needs;
    }

    // ── Inventory › Stock ───────────────────────────────────────────

    public static IReadOnlyList<string> StockFilters { get; } = new[] { "All", "Low stock", "Needed by orders", "Profiles", "Glass", "Hardware and accessories" };

    public ObservableCollection<StockRow> Stock { get; } = new();
    public ObservableCollection<IssueRow> Issues { get; } = new();
    public ObservableCollection<StockMoveRow> Moves { get; } = new();

    /// <summary>Every library item, to start keeping stock of it or to add it to a purchase order.</summary>
    public IReadOnlyList<StockItemChoice> Choices { get; private set; } = Array.Empty<StockItemChoice>();

    private string _stockFilter = StockFilters[0];
    public string StockFilter { get => _stockFilter; set { if (SetProperty(ref _stockFilter, value ?? StockFilters[0])) ShowStock(); } }

    private string _stockSearch = "";
    public string StockSearch { get => _stockSearch; set { if (SetProperty(ref _stockSearch, value)) ShowStock(); } }

    public int LowCount => _positions.Count(p => p.IsLow);

    /// <summary>"3 items are low on stock" (or empty).</summary>
    public string LowText => LowCount switch
    {
        0 => "",
        1 => "1 item is low on stock: see what to buy in Purchasing.",
        int n => $"{n} items are low on stock: see what to buy in Purchasing."
    };

    public bool HasLow => LowCount > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool IsStockEmpty => Stock.Count == 0;

    private StockRow? _selectedStock;
    public StockRow? SelectedStock
    {
        get => _selectedStock;
        set
        {
            if (!SetProperty(ref _selectedStock, value)) return;
            OnPropertyChanged(nameof(HasSelectedStock));
            ReorderText = value is null ? "" : Number(value.Position.ReorderLevel);
            LocationText = value?.Position.Location ?? "";
            AdjustText = "";
            CountText = "";
            NoteText = "";
            ShowMoves();
        }
    }

    public bool HasSelectedStock => _selectedStock is not null;

    private string _adjustText = "";
    /// <summary>How much to add (+) or take out (−).</summary>
    public string AdjustText { get => _adjustText; set => SetProperty(ref _adjustText, value); }

    private string _countText = "";
    /// <summary>What a stock count found (sets on hand to it).</summary>
    public string CountText { get => _countText; set => SetProperty(ref _countText, value); }

    private string _noteText = "";
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }

    private string _reorderText = "";
    public string ReorderText { get => _reorderText; set => SetProperty(ref _reorderText, value); }

    private string _locationText = "";
    public string LocationText { get => _locationText; set => SetProperty(ref _locationText, value); }

    private StockItemChoice? _newStockItem;
    public StockItemChoice? NewStockItem { get => _newStockItem; set => SetProperty(ref _newStockItem, value); }

    public ICommand AdjustCommand { get; }
    public ICommand CountCommand { get; }
    public ICommand SaveStockSettingsCommand { get; }
    public ICommand AddStockItemCommand { get; }

    private void ShowStock()
    {
        var selected = _selectedStock?.Position.Key;
        Stock.Clear();
        string search = (_stockSearch ?? "").Trim();
        foreach (var p in _positions.Where(p => _stockFilter switch
                 {
                     "Low stock" => p.IsLow,
                     "Needed by orders" => p.Reserved > 0,
                     "Profiles" => p.Key.Kind == StockKind.Profile,
                     "Glass" => p.Key.Kind == StockKind.Glass,
                     "Hardware and accessories" => p.Key.Kind == StockKind.Material,
                     _ => true
                 }).Where(p => search.Length == 0 || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                                 || p.Key.ItemId.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            Stock.Add(new StockRow(p, KindName(p.Key.Kind), Number(p.OnHand), Number(p.Reserved), Number(p.Available), Number(p.OnOrder),
                p.ReorderLevel > 0 ? Number(p.ReorderLevel) : "—", p.Shortfall > 0 ? Number(p.Shortfall) : "", string.Join(", ", p.ForOrders)));
        }
        _selectedStock = selected is { } key ? Stock.FirstOrDefault(r => r.Position.Key == key) : null;
        OnPropertyChanged(nameof(SelectedStock));
        OnPropertyChanged(nameof(HasSelectedStock));
        OnPropertyChanged(nameof(LowCount));
        OnPropertyChanged(nameof(LowText));
        OnPropertyChanged(nameof(HasLow));
        OnPropertyChanged(nameof(IsStockEmpty));
        ShowMoves();
    }

    private void ShowMoves()
    {
        Moves.Clear();
        if (_store() is not { } store) return;
        try
        {
            var library = _library();
            foreach (var m in store.Inventory.Moves(_selectedStock?.Position.Key, 100))
                Moves.Add(new StockMoveRow(m.Utc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture),
                    StockNeeds.NameOf(new StockKey(m.Kind, m.ItemId), library),
                    (m.Quantity > 0 ? "+" : "") + Number(m.Quantity), m.Reason.ToString(), m.Reference, m.By, m.Note));
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    private void ShowIssues()
    {
        Issues.Clear();
        foreach (var (order, needs) in _toIssue)
        {
            var id = order.Id;
            Issues.Add(new IssueRow(order.Id, order.OrderNumber, $"{order.ProjectName} · {order.ClientName}".Trim(' ', '·'),
                needs.Count == 0 ? "nothing from stock" : string.Join(", ", needs.Take(4).Select(n => $"{n.Name} {Number(n.Quantity)} {n.Unit}"))
                                                          + (needs.Count > 4 ? $" and {needs.Count - 4} more" : ""),
                new RelayCommand(() => Report(Issue(id)))));
        }
        OnPropertyChanged(nameof(HasIssues));
    }

    /// <summary>Adds or takes out <see cref="AdjustText"/> of the selected item. Returns an error, or null.</summary>
    public string? Adjust()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_selectedStock is not { } row) return "Choose an item first.";
        if (!TryNumber(AdjustText, out double quantity) || quantity == 0) return "Enter how much to add (e.g. 10) or take out (e.g. -2).";
        return Book(row.Position.Key, quantity, StockMoveReason.Adjusted, NoteText.Trim().Length > 0 ? NoteText.Trim() : "Adjusted",
            $"{(quantity > 0 ? "Added" : "Took out")} {Number(Math.Abs(quantity))} {row.Unit} of {row.Name}.");
    }

    /// <summary>Sets on hand of the selected item to a stock count (<see cref="CountText"/>). Returns an error, or null.</summary>
    public string? Count()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_selectedStock is not { } row) return "Choose an item first.";
        if (!TryNumber(CountText, out double counted) || counted < 0) return "Enter what the count found (0 or more).";
        double difference = counted - row.Position.OnHand;
        if (Math.Abs(difference) < 1e-9) return $"{row.Name} is already {Number(counted)} {row.Unit}.";
        return Book(row.Position.Key, difference, StockMoveReason.Adjusted, NoteText.Trim().Length > 0 ? NoteText.Trim() : "Stock count",
            $"{row.Name} is now {Number(counted)} {row.Unit} ({(difference > 0 ? "+" : "")}{Number(difference)}).");
    }

    private string? Book(StockKey key, double quantity, StockMoveReason reason, string reference, string done)
    {
        try
        {
            _store()!.Inventory.Move(new[] { (key, quantity) }, reason, reference, _user(), NoteText.Trim());
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        LoadAll();
        AdjustText = "";
        CountText = "";
        NoteText = "";
        Show(done, false);
        return null;
    }

    /// <summary>Saves the selected item's reorder level and location.</summary>
    public string? SaveStockSettings()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_selectedStock is not { } row) return "Choose an item first.";
        double reorder = 0;
        if (ReorderText.Trim().Length > 0 && (!TryNumber(ReorderText, out reorder) || reorder < 0))
            return "Enter the reorder level as a number (0: no alert).";
        try
        {
            _store()!.Inventory.SetLevel(row.Position.Key, reorder, LocationText);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        LoadAll();
        Show($"Saved {row.Name}: {(reorder > 0 ? $"buy more when {Number(reorder)} {row.Unit} or less are free" : "no low-stock alert")}.", false);
        return null;
    }

    /// <summary>Starts keeping stock of <see cref="NewStockItem"/> (so it can be counted and given a reorder level).</summary>
    public string? AddStockItem()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_newStockItem is not { } item) return "Choose the item to keep in stock.";
        try
        {
            if (!_positions.Any(p => p.Key == item.Key))
                _store()!.Inventory.SetLevel(item.Key, 0, "");
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        _stockFilter = StockFilters[0];
        _stockSearch = "";
        OnPropertyChanged(nameof(StockFilter));
        OnPropertyChanged(nameof(StockSearch));
        LoadAll();
        SelectedStock = Stock.FirstOrDefault(r => r.Position.Key == item.Key);
        Show($"{item.Name} is in the stock list: enter a count to set what is on hand.", false);
        return null;
    }

    /// <summary>Takes everything a production order needs out of stock (once). Returns an error, or null.</summary>
    public string? Issue(Guid orderId)
    {
        if (Blocked() is { } blocked) return blocked;
        var (order, needs) = _toIssue.FirstOrDefault(t => t.Order.Id == orderId);
        if (order is null) return "That production order's stock was already issued.";
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Issue stock",
                $"Take what {order.OrderNumber} needs out of stock ({needs.Count} items)? Do this when the material goes to the workshop."))
            return null;
        var moves = needs.Select(n => (n.Key, -n.Quantity)).ToList();
        // A job with nothing from stock still gets a move (of no item), so it counts as issued.
        if (moves.Count == 0) moves.Add((new StockKey(StockKind.Material, ""), 0));
        try
        {
            _store()!.Inventory.Move(moves, StockMoveReason.Issued, order.OrderNumber, _user(), order.ProjectName);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        var short_ = needs.Where(n => (_positions.FirstOrDefault(p => p.Key == n.Key)?.OnHand ?? 0) < n.Quantity).Select(n => n.Name).ToList();
        LoadAll();
        Show($"Issued the stock of {order.OrderNumber}." + (short_.Count == 0 ? ""
            : $" There was not enough of {string.Join(", ", short_)}: their stock is now below zero, so count them when the goods come in."), short_.Count > 0);
        return null;
    }

    // ── Purchasing › Purchase orders ────────────────────────────────

    public ObservableCollection<PurchaseRow> Purchases { get; } = new();
    public ObservableCollection<PurchaseLineRow> Lines { get; } = new();

    public static IReadOnlyList<string> PurchaseFilters { get; } = new[] { "Open", "Draft", "Ordered", "Received", "Cancelled", "All" };

    private string _purchaseFilter = PurchaseFilters[0];
    public string PurchaseFilter { get => _purchaseFilter; set { if (SetProperty(ref _purchaseFilter, value ?? PurchaseFilters[0])) ShowPurchases(); } }

    /// <summary>What open orders and reorder levels need bought, after stock and what is on order.</summary>
    public string ShortfallText
    {
        get
        {
            var short_ = _positions.Where(p => p.Shortfall > 0).ToList();
            return short_.Count == 0 ? "Nothing to buy: stock and what is on order cover the open production orders and reorder levels."
                : $"To buy: {string.Join(", ", short_.Take(6).Select(p => $"{p.Name} {Number(p.Shortfall)} {p.Unit}"))}{(short_.Count > 6 ? $" and {short_.Count - 6} more" : "")}.";
        }
    }

    private PurchaseRow? _selectedPurchase;
    public PurchaseRow? SelectedPurchase
    {
        get => _selectedPurchase;
        set
        {
            if (!SetProperty(ref _selectedPurchase, value)) return;
            ShowPurchase();
        }
    }

    public PurchaseOrder? Purchase => _selectedPurchase?.Order;
    public bool HasPurchase => Purchase is not null;
    public bool IsDraft => Purchase?.Status == PurchaseStatus.Draft;

    /// <summary>The lines cannot be changed any more (placed, received or cancelled).</summary>
    public bool IsLocked => !IsDraft;
    public bool CanReceive => Purchase?.Status is PurchaseStatus.Ordered or PurchaseStatus.PartReceived;
    public string PurchaseTitle => Purchase is { } p ? $"{p.Number} · {p.SupplierName}" : "";
    public string PurchaseStatusText => Purchase is { } p ? PurchaseOrder.StatusName(p.Status) : "";
    public string PurchaseTotalText => Purchase is { } p ? Money(p.Total) : "";

    /// <summary>The goods receipts of the open purchase order.</summary>
    public string ReceiptsText => Purchase is { Receipts.Count: > 0 } p
        ? string.Join("\n", p.Receipts.Select(r => $"{r.Number} · {r.Utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)} · {r.By}"
                                                   + (r.SupplierInvoice.Length > 0 ? $" · invoice {r.SupplierInvoice}" : "")
                                                   + $" · {r.Lines.Count} item{(r.Lines.Count == 1 ? "" : "s")}"))
        : "";

    private Supplier? _purchaseSupplier;
    /// <summary>The supplier of a new purchase order.</summary>
    public Supplier? PurchaseSupplier { get => _purchaseSupplier; set => SetProperty(ref _purchaseSupplier, value); }

    private StockItemChoice? _lineItem;
    public StockItemChoice? LineItem { get => _lineItem; set => SetProperty(ref _lineItem, value); }

    private string _lineQuantityText = "";
    public string LineQuantityText { get => _lineQuantityText; set => SetProperty(ref _lineQuantityText, value); }

    private string _purchaseNote = "";
    public string PurchaseNote { get => _purchaseNote; set => SetProperty(ref _purchaseNote, value); }

    private string _expectedText = "";
    /// <summary>When the goods are wanted (dd-mm-yyyy), or empty.</summary>
    public string ExpectedText { get => _expectedText; set => SetProperty(ref _expectedText, value); }

    private string _supplierInvoice = "";
    public string SupplierInvoice { get => _supplierInvoice; set => SetProperty(ref _supplierInvoice, value); }

    public ICommand SuggestCommand { get; }
    public ICommand NewPurchaseCommand { get; }
    public ICommand AddLineCommand { get; }
    public ICommand SavePurchaseCommand { get; }
    public ICommand PlaceOrderCommand { get; }
    public ICommand CancelPurchaseCommand { get; }
    public ICommand ReceiveCommand { get; }
    public ICommand PurchasePdfCommand { get; }

    private void ShowPurchases()
    {
        var selected = _selectedPurchase?.Order.Id;
        Purchases.Clear();
        // The open one stays in the list whatever the filter (e.g. just received in full while showing "Open").
        foreach (var p in _purchases.Where(p => p.Id == selected || _purchaseFilter switch
                 {
                     "Open" => p.Status is PurchaseStatus.Draft or PurchaseStatus.Ordered or PurchaseStatus.PartReceived,
                     "Draft" => p.Status == PurchaseStatus.Draft,
                     "Ordered" => p.Status is PurchaseStatus.Ordered or PurchaseStatus.PartReceived,
                     "Received" => p.Status == PurchaseStatus.Received,
                     "Cancelled" => p.Status == PurchaseStatus.Cancelled,
                     _ => true
                 }))
        {
            Purchases.Add(new PurchaseRow(p, p.CreatedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                PurchaseOrder.StatusName(p.Status), Money(p.Total)));
        }
        _selectedPurchase = selected is { } id ? Purchases.FirstOrDefault(r => r.Order.Id == id) : null;
        OnPropertyChanged(nameof(SelectedPurchase));
        OnPropertyChanged(nameof(ShortfallText));
        ShowPurchase();
    }

    private void ShowPurchase()
    {
        Lines.Clear();
        if (Purchase is { } p)
        {
            foreach (var line in p.Lines)
                Lines.Add(new PurchaseLineRow(line, p.ReceivedOf(line.Key), RemoveLine));
            PurchaseNote = p.Note;
            ExpectedText = p.ExpectedDate?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";
        }
        SupplierInvoice = "";
        foreach (string name in new[] { nameof(Purchase), nameof(HasPurchase), nameof(IsDraft), nameof(IsLocked), nameof(CanReceive), nameof(PurchaseTitle),
                     nameof(PurchaseStatusText), nameof(PurchaseTotalText), nameof(ReceiptsText) })
            OnPropertyChanged(name);
    }

    /// <summary>
    /// A draft purchase order to <see cref="PurchaseSupplier"/> with everything short that it supplies (all of it when it
    /// supplies anything), at library prices. Returns an error, or null.
    /// </summary>
    public string? Suggest()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_purchaseSupplier is not { } supplier) return "Choose the supplier first (add one in Suppliers).";
        var library = _library();
        var lines = _positions.Where(p => p.Shortfall > 0 && (supplier.Supplies.Count == 0 || supplier.Supplies.Contains(p.Key.Kind)))
            .Select(p => new PurchaseLine(p.Key.Kind, p.Key.ItemId, p.Name, p.Unit, Math.Ceiling(p.Shortfall * 100) / 100, StockNeeds.RateOf(p.Key, library)))
            .ToList();
        if (lines.Count == 0) return $"Nothing {supplier.Name} supplies is short right now.";
        var orders = _positions.Where(p => lines.Any(l => l.Key == p.Key)).SelectMany(p => p.ForOrders).Distinct().OrderBy(o => o).ToList();
        return Create(supplier, lines, string.Join(", ", orders),
            $"Made {{0}} for {supplier.Name} with {lines.Count} item{(lines.Count == 1 ? "" : "s")} that are short. Check it, then place the order.");
    }

    /// <summary>An empty draft purchase order to <see cref="PurchaseSupplier"/>. Returns an error, or null.</summary>
    public string? NewPurchase()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_purchaseSupplier is not { } supplier) return "Choose the supplier first (add one in Suppliers).";
        return Create(supplier, new List<PurchaseLine>(), "", $"Made {{0}} for {supplier.Name}: add its items.");
    }

    private string? Create(Supplier supplier, List<PurchaseLine> lines, string forOrders, string done)
    {
        var order = new PurchaseOrder { SupplierId = supplier.Id, SupplierName = supplier.Name, CreatedBy = _user(), Lines = lines, ForOrders = forOrders };
        try
        {
            _store()!.Inventory.SavePurchaseOrder(order);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        _purchaseFilter = "Open";
        OnPropertyChanged(nameof(PurchaseFilter));
        LoadAll();
        SelectedPurchase = Purchases.FirstOrDefault(r => r.Order.Id == order.Id);
        Show(string.Format(CultureInfo.InvariantCulture, done, order.Number), false);
        return null;
    }

    /// <summary>Adds <see cref="LineItem"/> × <see cref="LineQuantityText"/> to the draft. Returns an error, or null.</summary>
    public string? AddLine()
    {
        if (Purchase is not { Status: PurchaseStatus.Draft }) return "Only a draft purchase order can be changed.";
        if (_lineItem is not { } item) return "Choose the item to add.";
        if (!TryNumber(LineQuantityText, out double quantity) || quantity <= 0) return "Enter how much to buy.";
        if (TakeLines() is { } error) return error;
        Purchase.Lines.Add(new PurchaseLine(item.Key.Kind, item.Key.ItemId, item.Name, item.Unit, quantity, StockNeeds.RateOf(item.Key, _library())));
        LineQuantityText = "";
        return SaveOpen($"Added {item.Name}.");
    }

    private void RemoveLine(PurchaseLineRow row)
    {
        if (Purchase is not { Status: PurchaseStatus.Draft } p) return;
        if (TakeLines() is { } error)
        {
            Show(error, true);
            return;
        }
        int index = Lines.IndexOf(row);
        if (index >= 0 && index < p.Lines.Count) p.Lines.RemoveAt(index);
        Report(SaveOpen($"Removed {row.Name}."));
    }

    /// <summary>Saves the draft's quantities, rates, note and wanted date.</summary>
    public string? SavePurchase()
    {
        if (Blocked() is { } blocked) return blocked;
        if (Purchase is not { } p) return "Choose a purchase order.";
        if (p.Status == PurchaseStatus.Draft && TakeLines() is { } error) return error;
        if (TakeDetails() is { } detailsError) return detailsError;
        return SaveOpen($"Saved {p.Number}.");
    }

    /// <summary>Places the draft with the supplier: from now on its items are on order. Returns an error, or null.</summary>
    public string? PlaceOrder()
    {
        if (Blocked() is { } blocked) return blocked;
        if (Purchase is not { Status: PurchaseStatus.Draft } p) return "Only a draft purchase order can be placed.";
        if (TakeLines() is { } error) return error;
        if (TakeDetails() is { } detailsError) return detailsError;
        if (p.Lines.Count == 0) return "Add the items to buy first.";
        p.OrderedUtc = DateTime.UtcNow;
        return SaveOpen($"Placed {p.Number} with {p.SupplierName}: its items are on order. Make the PDF to send it.");
    }

    public string? CancelPurchase()
    {
        if (Blocked() is { } blocked) return blocked;
        if (Purchase is not { } p || p.Status is PurchaseStatus.Received or PurchaseStatus.Cancelled)
            return "Only an open purchase order can be cancelled.";
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Cancel purchase order", $"Cancel {p.Number} to {p.SupplierName}? Goods already received stay in stock."))
            return null;
        p.IsCancelled = true;
        return SaveOpen($"Cancelled {p.Number}.");
    }

    /// <summary>Books what came in (each line's <see cref="PurchaseLineRow.ReceiveText"/>) into stock as a goods receipt.</summary>
    public string? Receive()
    {
        if (Blocked() is { } blocked) return blocked;
        if (Purchase is not { } p || !CanReceive) return "Only an ordered purchase order can be received.";
        var lines = new List<ReceiptLine>();
        foreach (var row in Lines)
        {
            if (!TryNumber(row.ReceiveText, out double quantity) || quantity < 0) return $"Enter how much of {row.Name} came in (0 if none).";
            if (quantity > 0) lines.Add(new ReceiptLine(row.Line.Kind, row.Line.ItemId, quantity));
        }
        try
        {
            var receipt = _store()!.Inventory.Receive(p, lines, _user(), SupplierInvoice);
            LoadAll();
            Show($"Received {receipt.Number}: {lines.Count} item{(lines.Count == 1 ? "" : "s")} added to stock.", false);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Writes the purchase order PDF (to <paramref name="path"/>, or where the user chooses) and opens it.</summary>
    public string? PurchasePdfFile(string? path)
    {
        if (Purchase is not { } p) return "Choose a purchase order.";
        path ??= _dialogs()?.ChooseSaveFile("Save purchase order", "PDF files (*.pdf)|*.pdf", $"{p.Number} {string.Join("_", p.SupplierName.Split(Path.GetInvalidFileNameChars()))}.pdf");
        if (path is null) return null;
        var supplier = Suppliers.FirstOrDefault(s => s.Id == p.SupplierId);
        var head = _letterhead();
        var paper = new PurchasePaper
        {
            CompanyName = head.Name,
            CompanyLines = new[] { head.Address, head.Phone.Length > 0 ? $"Phone: {head.Phone}" : "" },
            SupplierName = p.SupplierName,
            SupplierLines = supplier is null ? Array.Empty<string>() : new[]
            {
                supplier.ContactPerson, supplier.Address, supplier.Phone.Length > 0 ? $"Phone: {supplier.Phone}" : "",
                supplier.Email, supplier.Gstin.Length > 0 ? $"GSTIN: {supplier.Gstin}" : ""
            },
            Number = p.Number,
            Date = (p.OrderedUtc ?? p.CreatedUtc).ToLocalTime().ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Expected = p.ExpectedDate?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "",
            ForOrders = p.ForOrders,
            Currency = _library().Currency,
            Lines = p.Lines.Select(l => new PurchasePaperLine(l.Name, Number(l.Quantity), l.Unit, Money(l.Rate), Money(l.Amount))).ToList(),
            Total = Money(p.Total),
            Note = p.Note
        };
        try
        {
            using (var stream = File.Create(path))
                PurchasePdf.Write(paper, stream);
            OpenDocument?.Invoke(path);
            Show($"Saved {Path.GetFileName(path)}.", false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The purchase order could not be written: {ex.Message}";
        }
    }

    /// <summary>Reads the draft's quantities and rates from the rows into the order.</summary>
    private string? TakeLines()
    {
        if (Purchase is not { } p) return null;
        for (int i = 0; i < Lines.Count && i < p.Lines.Count; i++)
        {
            var row = Lines[i];
            if (!TryNumber(row.QuantityText, out double quantity) || quantity <= 0) return $"Enter how much {row.Name} to buy.";
            if (!PricingViewModel.TryParseDecimal(row.RateText, out decimal rate) || rate < 0) return $"Enter the rate of {row.Name}.";
            p.Lines[i] = p.Lines[i] with { Quantity = quantity, Rate = rate };
        }
        return null;
    }

    private string? TakeDetails()
    {
        if (Purchase is not { } p) return null;
        DateTime? expected = null;
        if (ExpectedText.Trim().Length > 0)
        {
            if (!DateTime.TryParseExact(ExpectedText.Trim(), new[] { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return "Enter the date wanted as dd-mm-yyyy, or leave it empty.";
            expected = date;
        }
        p.ExpectedDate = expected;
        p.Note = (PurchaseNote ?? "").Trim();
        return null;
    }

    private string? SaveOpen(string done)
    {
        if (Purchase is not { } p) return null;
        try
        {
            _store()!.Inventory.SavePurchaseOrder(p);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        LoadAll();
        SelectedPurchase = Purchases.FirstOrDefault(r => r.Order.Id == p.Id);
        Show(done, false);
        return null;
    }

    // ── Purchasing › Suppliers ──────────────────────────────────────

    public ObservableCollection<Supplier> Suppliers { get; } = new();

    /// <summary>The active suppliers, for new purchase orders.</summary>
    public ObservableCollection<Supplier> ActiveSuppliers { get; } = new();

    private Supplier? _selectedSupplier;
    public Supplier? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (!SetProperty(ref _selectedSupplier, value)) return;
            Editing = value?.Copy();
        }
    }

    private Supplier? _editing;
    /// <summary>The supplier in the form (a copy until saved).</summary>
    public Supplier? Editing
    {
        get => _editing;
        private set
        {
            SetProperty(ref _editing, value);
            OnPropertyChanged(nameof(IsEditingSupplier));
            OnPropertyChanged(nameof(SuppliesProfiles));
            OnPropertyChanged(nameof(SuppliesGlass));
            OnPropertyChanged(nameof(SuppliesMaterials));
        }
    }

    public bool IsEditingSupplier => _editing is not null;

    public bool SuppliesProfiles { get => Supplies(StockKind.Profile); set => SetSupplies(StockKind.Profile, value); }
    public bool SuppliesGlass { get => Supplies(StockKind.Glass); set => SetSupplies(StockKind.Glass, value); }
    public bool SuppliesMaterials { get => Supplies(StockKind.Material); set => SetSupplies(StockKind.Material, value); }

    private bool Supplies(StockKind kind) => _editing?.Supplies.Contains(kind) == true;

    private void SetSupplies(StockKind kind, bool on)
    {
        if (_editing is null) return;
        _editing.Supplies.Remove(kind);
        if (on) _editing.Supplies.Add(kind);
        _editing.Supplies.Sort();
    }

    public ICommand NewSupplierCommand { get; }
    public ICommand SaveSupplierCommand { get; }

    private void LoadSuppliers(LocalStore store)
    {
        var selected = _selectedSupplier?.Id;
        var chosen = _purchaseSupplier?.Id;
        Suppliers.Clear();
        ActiveSuppliers.Clear();
        foreach (var s in store.Inventory.Suppliers())
        {
            Suppliers.Add(s);
            if (s.IsActive) ActiveSuppliers.Add(s);
        }
        _selectedSupplier = Suppliers.FirstOrDefault(s => s.Id == selected);
        OnPropertyChanged(nameof(SelectedSupplier));
        _purchaseSupplier = ActiveSuppliers.FirstOrDefault(s => s.Id == chosen) ?? ActiveSuppliers.FirstOrDefault();
        OnPropertyChanged(nameof(PurchaseSupplier));
    }

    private void NewSupplier()
    {
        _selectedSupplier = null;
        OnPropertyChanged(nameof(SelectedSupplier));
        Editing = new Supplier { Supplies = { StockKind.Profile, StockKind.Glass, StockKind.Material } };
    }

    public string? SaveSupplier()
    {
        if (Blocked() is { } blocked) return blocked;
        if (_editing is not { } s) return "Choose a supplier, or New supplier.";
        s.Name = (s.Name ?? "").Trim();
        if (s.Name.Length == 0) return "Give the supplier a name.";
        if (s.Name.Length > 100) return "The supplier's name can be at most 100 characters.";
        if (Suppliers.Any(x => x.Id != s.Id && string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
            return $"There is already a supplier called {s.Name}.";
        foreach (var field in new[] { s.ContactPerson, s.Phone, s.Email, s.Address, s.Gstin, s.Note })
            if ((field ?? "").Length > 500) return "A supplier's details can be at most 500 characters each.";
        try
        {
            _store()!.Inventory.SaveSupplier(s);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        var id = s.Id;
        LoadAll();
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == id);
        Show($"Saved the supplier {s.Name}.", false);
        return null;
    }

    // ── Formatting ──────────────────────────────────────────────────

    public static string KindName(StockKind kind) => kind switch
    {
        StockKind.Profile => "Profile",
        StockKind.Glass => "Glass",
        _ => "Hardware / accessory"
    };

    public static string Number(double value) => Math.Round(value, 2).ToString("#,##0.##", CultureInfo.InvariantCulture);

    private string Money(decimal value) => $"{value.ToString("N2", CultureInfo.InvariantCulture)} {_library().Currency}".TrimEnd();

    private static bool TryNumber(string? text, out double value)
    {
        text = (text ?? "").Trim().Replace(",", "");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
