using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Production;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>A production order in the list: "OR-00012 · Sharma residence · 3 windows · All cut · 40 %".</summary>
public sealed record ProductionRow(Guid Id, string OrderNumber, string Title, string Detail, string StageText, double Fraction)
{
    public string PercentText => $"{Fraction * 100:0} %";
}

/// <summary>A confirmed order that has no production order yet.</summary>
public sealed record OrderChoice(Guid ProjectId, string OrderNumber, string Title)
{
    public override string ToString() => $"{OrderNumber}  ·  {Title}";
}

/// <summary>One step of one design: "2 / 3" windows done; a click marks all or none, − and + one window.</summary>
public sealed class StepCell : ViewModelBase
{
    private readonly Action<StepCell, int> _set;

    public StepCell(ProductionStep step, int done, int quantity, bool canChange, Action<StepCell, int> set)
    {
        Step = step;
        _done = done;
        Quantity = quantity;
        _set = set;
        ToggleCommand = new RelayCommand(() => _set(this, IsComplete ? 0 : Quantity), () => canChange);
        MoreCommand = new RelayCommand(() => _set(this, Done + 1), () => canChange && Done < Quantity);
        LessCommand = new RelayCommand(() => _set(this, Done - 1), () => canChange && Done > 0);
    }

    public ProductionStep Step { get; }
    public string Name => ProductionOrder.StepName(Step);
    public int Quantity { get; }

    private int _done;
    public int Done
    {
        get => _done;
        set
        {
            if (!SetProperty(ref _done, value)) return;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(IsComplete));
            OnPropertyChanged(nameof(IsStarted));
            ((RelayCommand)MoreCommand).RaiseCanExecuteChanged();
            ((RelayCommand)LessCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>"2 / 3", or "✓" / "–" for a single window.</summary>
    public string Text => Quantity == 1 ? (Done > 0 ? "✓" : "–") : $"{Done} / {Quantity}";
    public bool IsComplete => Done >= Quantity;
    public bool IsStarted => Done > 0 && !IsComplete;
    public bool HasCounts => Quantity > 1;

    public ICommand ToggleCommand { get; }
    public ICommand MoreCommand { get; }
    public ICommand LessCommand { get; }
}

/// <summary>A design of the open production order with its progress per step.</summary>
public sealed record WindowRow(Guid FrameId, string Reference, string Name, string SizeText, int Quantity, IReadOnlyList<StepCell> Steps);

/// <summary>
/// Production › Production orders (Milestone 16): every production order with its stage and progress; a new one from a
/// confirmed order; the chosen order's windows with their progress (cut, assembled, glazed, ready, dispatched), its due
/// date and notes, and its workshop papers — cutting list, glass order, hardware pick list, shop drawings and labels.
/// After cutting, the offcuts in stock are updated from the cutting list.
/// </summary>
public sealed class ProductionViewModel : ViewModelBase
{
    private readonly Func<LocalStore?> _store;
    private readonly Func<IProductLibrary> _library;
    private readonly Func<CalculationRules> _rules;
    private readonly Func<DesignRules> _designRules;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<string> _companyName;
    private ProductionOrder? _order;

    public ProductionViewModel(Func<LocalStore?> store, Func<IProductLibrary> library, Func<CalculationRules> rules,
        Func<DesignRules> designRules, Func<IDialogService?> dialogs, Func<string> companyName)
    {
        _store = store;
        _library = library;
        _rules = rules;
        _designRules = designRules;
        _dialogs = dialogs;
        _companyName = companyName;
        CreateCommand = new RelayCommand(() => Report(Create(NewFrom?.ProjectId)), () => NewFrom is not null && CanChange);
        DeleteCommand = new RelayCommand(() => Report(Delete()), () => _order is not null && CanChange);
        PaperCommand = new RelayCommand(p => Report(MakePaper(p is ProductionSheet s ? s : Enum.Parse<ProductionSheet>(p?.ToString() ?? ""))),
            _ => _order is not null);
        UpdateOffcutsCommand = new RelayCommand(() => Report(UpdateOffcuts()), () => _order is { OffcutsUpdatedUtc: null } && CanChange);
        MarkAllCommand = new RelayCommand(p => Report(MarkAll(p is ProductionStep s ? s : Enum.Parse<ProductionStep>(p?.ToString() ?? ""))),
            _ => _order is not null && CanChange);
    }

    /// <summary>Why changes are refused (read-only licence, or the feature not given), or null.</summary>
    public Func<string?> Blocked { get; set; } = () => null;

    /// <summary>Opens a written paper with the computer's PDF viewer.</summary>
    public Action<string>? OpenDocument { get; set; }

    public bool CanChange => Blocked() is null;

    public ObservableCollection<ProductionRow> Orders { get; } = new();
    public ObservableCollection<OrderChoice> AvailableOrders { get; } = new();
    public ObservableCollection<WindowRow> Windows { get; } = new();

    public bool HasOrders => Orders.Count > 0;
    public bool HasAvailableOrders => AvailableOrders.Count > 0;

    private OrderChoice? _newFrom;
    /// <summary>The confirmed order to make a production order for.</summary>
    public OrderChoice? NewFrom
    {
        get => _newFrom;
        set
        {
            if (SetProperty(ref _newFrom, value)) ((RelayCommand)CreateCommand).RaiseCanExecuteChanged();
        }
    }

    private ProductionRow? _selected;
    private bool _replacingRow;
    public ProductionRow? Selected
    {
        get => _selected;
        set
        {
            if (_replacingRow) return;                                     // the list briefly loses its choice while a row is replaced
            if (!SetProperty(ref _selected, value)) return;
            Open(value?.Id);
        }
    }

    public bool HasOrder => _order is not null;

    public string OrderTitle => _order is null ? "" : $"{_order.OrderNumber}  ·  {_order.ProjectName}";

    public string OrderDetail => _order is null ? ""
        : string.Join("  ·  ", new[]
        {
            _order.QuoteNumber, _order.ClientName,
            $"started {_order.CreatedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
            + (_order.CreatedBy.Length > 0 ? $" by {_order.CreatedBy}" : "")
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string StageText => _order is null ? "" : _order.StageText(Quantities());

    public double Fraction => _order is null ? 0 : _order.Fraction(Quantities());

    public string PercentText => $"{Fraction * 100:0} % done";

    public string OffcutsText => _order?.OffcutsUpdatedUtc is { } at
        ? $"Offcuts in stock were updated from this order's cutting on {at.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
        : "After cutting, Update offcuts takes the offcuts used out of stock and puts this order's new offcuts in.";

    private bool _useOffcuts = true;
    /// <summary>The cutting list cuts from offcuts in stock first.</summary>
    public bool UseOffcuts { get => _useOffcuts; set => SetProperty(ref _useOffcuts, value); }

    /// <summary>The due date (changing it saves the order).</summary>
    public DateTime? DueDate
    {
        get => _order?.DueDate;
        set
        {
            if (_order is null || _order.DueDate == value?.Date) return;
            _order.DueDate = value?.Date;
            OnPropertyChanged();
            Report(SaveOrder(), quiet: true);
        }
    }

    private string _notes = "";
    /// <summary>Notes for the workshop (saved when changed; the page sends them when the box is left).</summary>
    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value ?? "")) SaveNotes();
        }
    }

    /// <summary>Saves the notes (when they were changed).</summary>
    public void SaveNotes()
    {
        if (_order is null || _order.Notes == _notes.Trim()) return;
        _order.Notes = _notes.Trim();
        Report(SaveOrder(), quiet: true);
    }

    public static IReadOnlyList<ProductionStep> Steps => ProductionOrder.Steps;

    public ICommand CreateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PaperCommand { get; }
    public ICommand UpdateOffcutsCommand { get; }
    public ICommand MarkAllCommand { get; }

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

    // ── List ────────────────────────────────────────────────────────

    /// <summary>Reads the production orders and the confirmed orders without one.</summary>
    public void Reload(Guid? select = null)
    {
        if (_store() is not { } store) return;
        var keep = select ?? _selected?.Id;
        try
        {
            var orders = store.Production.List();
            Orders.Clear();
            foreach (var order in orders)
            {
                var quantities = QuantitiesOf(order, store);
                Orders.Add(new ProductionRow(order.Id, order.OrderNumber, order.ProjectName,
                    string.Join("  ·  ", new[] { order.ClientName, order.DueDate is { } due ? $"due {due.ToString("d MMM", CultureInfo.InvariantCulture)}" : "" }
                        .Where(t => t.Length > 0)),
                    order.StageText(quantities), order.Fraction(quantities)));
            }
            var started = orders.Select(o => o.ProjectId).ToHashSet();
            AvailableOrders.Clear();
            foreach (var p in store.Projects.List().Where(p => p.OrderNumber.Length > 0 && !started.Contains(p.Id))
                         .OrderByDescending(p => p.OrderNumber, StringComparer.Ordinal))
                AvailableOrders.Add(new OrderChoice(p.Id, p.OrderNumber, string.Join(" · ", new[] { p.Name, p.ClientName }.Where(t => t.Length > 0))));
            NewFrom = AvailableOrders.FirstOrDefault();
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(HasAvailableOrders));
        _selected = Orders.FirstOrDefault(o => o.Id == keep) ?? Orders.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        Open(_selected?.Id);
    }

    /// <summary>The quantity of each design of an order, read from its stored designs (the list reads them once).</summary>
    private static IReadOnlyDictionary<Guid, int> QuantitiesOf(ProductionOrder order, LocalStore store)
    {
        try
        {
            var full = order.DocumentJson.Length > 0 ? order : store.Production.Load(order.Id);
            return ProjectSerializer.Deserialize(full.DocumentJson).Frames.ToDictionary(f => f.Id, f => Math.Max(1, f.Design.Quantity));
        }
        catch (Exception ex) when (ex is DataStoreException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return new Dictionary<Guid, int>();
        }
    }

    private IReadOnlyDictionary<Guid, int> Quantities()
        => Windows.ToDictionary(w => w.FrameId, w => w.Quantity);

    private void Open(Guid? id)
    {
        _order = null;
        Windows.Clear();
        if (id is { } orderId && _store() is { } store)
        {
            try
            {
                _order = store.Production.Load(orderId);
                var project = ProjectSerializer.Deserialize(_order.DocumentJson);
                var refs = ProductionBuilder.Windows(project);
                foreach (var frame in project.Frames)
                {
                    var (reference, quantity) = refs[frame.Id];
                    var progress = _order.ProgressOf(frame.Id);
                    var cells = Steps.Select(s => new StepCell(s, Math.Min(progress.DoneAt(s), quantity), quantity, CanChange, SetDone)).ToList();
                    Windows.Add(new WindowRow(frame.Id, reference, frame.Design.Name,
                        $"{frame.Width.ToString("0.#", CultureInfo.InvariantCulture)} × {frame.Height.ToString("0.#", CultureInfo.InvariantCulture)}",
                        quantity, cells));
                }
            }
            catch (Exception ex) when (ex is DataStoreException or InvalidOperationException or System.Text.Json.JsonException)
            {
                _order = null;
                Windows.Clear();
                Show($"The production order could not be opened: {ex.Message}", true);
            }
        }
        _notes = _order?.Notes ?? "";
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(DueDate));
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        foreach (string name in new[] { nameof(HasOrder), nameof(OrderTitle), nameof(OrderDetail), nameof(StageText), nameof(Fraction),
                     nameof(PercentText), nameof(OffcutsText) })
            OnPropertyChanged(name);
        foreach (var command in new[] { DeleteCommand, PaperCommand, UpdateOffcutsCommand, MarkAllCommand })
            ((RelayCommand)command).RaiseCanExecuteChanged();
    }

    // ── Changes ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts production of a confirmed order: its designs as saved now are kept with the production order (later
    /// changes to the quote do not change it). Returns an error, or null.
    /// </summary>
    public string? Create(Guid? projectId)
    {
        if (Blocked() is { } blocked) return blocked;
        if (projectId is not { } id || _store() is not { } store) return "Choose the order to make.";
        try
        {
            if (store.Production.ForProject(id) is { } existing)
            {
                Reload(existing);
                return "This order is already in production; it is opened.";
            }
            var project = store.Projects.Load(id);
            if (project.Quote.OrderNumber.Length == 0) return "Only an order can be made: convert the quote to an order first.";
            if (project.Frames.Count == 0) return "The order has no designs.";
            var order = new ProductionOrder
            {
                ProjectId = project.Id,
                OrderNumber = project.Quote.OrderNumber,
                QuoteNumber = project.Quote.NumberText,
                ProjectName = project.Name,
                ClientName = project.Quote.Client.DisplayName,
                DocumentJson = ProjectSerializer.Serialize(project)
            };
            store.Production.Save(order);
            Reload(order.Id);
            Show($"Started production of {order.OrderNumber}: {project.Frames.Count} design{(project.Frames.Count == 1 ? "" : "s")}, " +
                 $"{project.Frames.Sum(f => Math.Max(1, f.Design.Quantity))} window{(project.Frames.Sum(f => f.Design.Quantity) == 1 ? "" : "s")}.", false);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private string? Delete()
    {
        if (_order is null || _store() is not { } store) return null;
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Delete production order",
                $"Delete the production order {_order.OrderNumber} and its progress? The order itself (the quote) stays."))
            return null;
        try
        {
            store.Production.Delete(_order.Id);
            string number = _order.OrderNumber;
            _selected = null;
            Reload();
            Show($"Deleted the production order {number}.", false);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private void SetDone(StepCell cell, int count)
    {
        if (_order is null || Blocked() is { } blocked)
        {
            if (Blocked() is { } why) Show(why, true);
            return;
        }
        var row = Windows.FirstOrDefault(w => w.Steps.Contains(cell));
        if (row is null) return;
        int value = Math.Clamp(count, 0, row.Quantity);
        _order.SetDone(row.FrameId, cell.Step, value, row.Quantity);
        cell.Done = value;
        if (SaveOrder() is { } error)
        {
            Show(error, true);
            return;
        }
        RefreshHeader();
        UpdateListRow();
    }

    /// <summary>Marks a step done for every window of the order.</summary>
    private string? MarkAll(ProductionStep step)
    {
        if (_order is null) return null;
        if (Blocked() is { } blocked) return blocked;
        foreach (var row in Windows)
        {
            _order.SetDone(row.FrameId, step, row.Quantity, row.Quantity);
            row.Steps.First(s => s.Step == step).Done = row.Quantity;
        }
        if (SaveOrder() is { } error) return error;
        RefreshHeader();
        UpdateListRow();
        Show($"Every window of {_order.OrderNumber} is {ProductionOrder.StepName(step).ToLowerInvariant()}.", false);
        return null;
    }

    private void UpdateListRow()
    {
        if (_order is null) return;
        int index = Orders.ToList().FindIndex(o => o.Id == _order.Id);
        if (index < 0) return;
        var quantities = Quantities();
        _replacingRow = true;
        try
        {
            Orders[index] = Orders[index] with { StageText = _order.StageText(quantities), Fraction = _order.Fraction(quantities) };
            _selected = Orders[index];
        }
        finally
        {
            _replacingRow = false;
        }
        OnPropertyChanged(nameof(Selected));
    }

    private string? SaveOrder()
    {
        if (_order is null || _store() is not { } store) return null;
        if (Blocked() is { } blocked) return blocked;
        try
        {
            store.Production.Save(_order);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    // ── Papers ──────────────────────────────────────────────────────

    /// <summary>The papers of the open order (offcuts in stock used when <see cref="UseOffcuts"/>). WPF: on the UI thread.</summary>
    public ProductionPapers? BuildPapers()
    {
        if (_order is null || _store() is not { } store) return null;
        var offcuts = UseOffcuts ? store.Production.Offcuts() : Array.Empty<Offcut>();
        return ProductionBuilder.Build(new ProductionInputs(_order, _library(), _rules(), _designRules(), offcuts, _companyName(), DateTime.UtcNow));
    }

    /// <summary>Writes a paper of the open order (to <paramref name="path"/>, or where the user chooses) and opens it.</summary>
    public string? MakePaper(ProductionSheet sheet, string? path = null)
    {
        if (_order is null) return "Choose a production order.";
        string name = string.Join("_", $"{_order.OrderNumber} {ProductionPdf.Title(sheet)}".Split(Path.GetInvalidFileNameChars())).Trim() + ".pdf";
        path ??= _dialogs()?.ChooseSaveFile($"Save {ProductionPdf.Title(sheet).ToLowerInvariant()}", "PDF files (*.pdf)|*.pdf", name);
        if (path is null) return null;
        try
        {
            var papers = BuildPapers()!;
            using (var stream = File.Create(path))
                ProductionPdf.Write(papers.Document, sheet, stream);
            Show($"Saved {Path.GetFileName(path)}.", false);
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or DataStoreException
                                       or System.Text.Json.JsonException)
        {
            return $"The {ProductionPdf.Title(sheet).ToLowerInvariant()} could not be written: {ex.Message}";
        }
    }

    /// <summary>
    /// After cutting the order: the offcuts its cutting list used leave stock and its new offcuts join it (once per
    /// order). Returns an error, or null.
    /// </summary>
    public string? UpdateOffcuts()
    {
        if (_order is null || _store() is not { } store) return null;
        if (Blocked() is { } blocked) return blocked;
        if (_order.OffcutsUpdatedUtc is not null) return $"The offcuts were already updated for {_order.OrderNumber}.";
        try
        {
            var plan = BuildPapers()!.Plan;
            var used = plan.OffcutIdsUsed.ToList();
            var leftovers = plan.Remnants.Select(r => (r.DefinitionId, r.LengthMm)).ToList();
            if (_dialogs() is { } dialogs && !dialogs.Confirm("Update offcuts",
                    $"Has {_order.OrderNumber} been cut as on its cutting list{(UseOffcuts ? "" : " (new bars only)")}? " +
                    $"{used.Count} offcut{(used.Count == 1 ? "" : "s")} will be taken out of stock and {leftovers.Count} new " +
                    $"offcut{(leftovers.Count == 1 ? "" : "s")} put in. This is done once for an order."))
                return null;
            var (taken, added) = store.Production.UpdateOffcuts(_order, used, leftovers);
            RefreshHeader();
            Show($"Offcuts updated: {taken} taken out of stock, {added} added.", false);
            return null;
        }
        catch (Exception ex) when (ex is DataStoreException or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    // ── Messages ────────────────────────────────────────────────────

    private void Report(string? error, bool quiet = false)
    {
        _ = quiet;
        if (error is not null) Show(error, true);
    }

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }
}
