using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>A category in the Documents tab's list, with how many documents it has.</summary>
public sealed class DocumentCategoryRow : ViewModelBase
{
    public DocumentCategoryRow(DocumentCategory category) => Category = category;

    public DocumentCategory Category { get; }
    public string Name => ProjectDocument.CategoryName(Category);

    /// <summary>What goes in the category (the tooltip).</summary>
    public string Description => Category switch
    {
        DocumentCategory.PreProductionSurvey => "The site survey before production: measurements, photos, site conditions",
        DocumentCategory.Quotations => "Every quotation PDF made for this quote (kept by MARK) and others you add",
        DocumentCategory.Margins => "Margin reports: cost, price and profit of the quote",
        DocumentCategory.CreditApproval => "Approval to give the client credit or a special discount",
        DocumentCategory.SalesOrder => "The signed order or contract with the client",
        DocumentCategory.TypologyHistory => "Typology = the window types (designs) of the quote: here, the designs of every saved revision, to compare",
        _ => "Anything else kept with the quote"
    };

    private int _count;
    public int Count { get => _count; set { if (SetProperty(ref _count, value)) OnPropertyChanged(nameof(CountText)); } }

    public string CountText => _count == 0 ? "" : _count.ToString(CultureInfo.InvariantCulture);

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public override string ToString() => Name;
}

/// <summary>A document of the quote, as a row: name, file, when and by whom, size, and what can be done with it.</summary>
public sealed record DocumentRow(ProjectDocument Document, string Added, string Size, ICommand OpenCommand, ICommand SaveCommand,
    ICommand DeleteCommand)
{
    public string Name => Document.Name;
    public string FileName => Document.FileName;
    public string AddedBy => Document.AddedBy;
    public string Kind => Path.GetExtension(Document.FileName).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext : "FILE";

    public override string ToString() => Name;
}

/// <summary>A saved revision of the quote with its designs (Typology History).</summary>
public sealed record TypologyRow(string Revision, string Saved, string Value, string Designs)
{
    public override string ToString() => $"{Revision} {Designs}";
}

/// <summary>
/// The open quote's Documents tab: its files by category (Pre Production Survey Report, Quotations, Margins, Credit
/// Approval, Sales Order/Contract, Others, Typology History), searchable and sortable. Upload adds any file; every
/// quotation MARK writes is kept under Quotations; "Make margin report" writes the margins PDF under Margins; Typology
/// History also lists the designs of every saved revision. Files are kept in the database with the quote.
/// </summary>
public sealed class DocumentsViewModel : ViewModelBase
{
    private readonly Func<LocalStore?> _store;
    private readonly Func<Project> _project;
    private readonly Func<bool> _isSaved;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<string> _userName;
    private readonly Func<string?> _readOnly;
    private readonly Func<MarginReport> _margins;
    private List<ProjectDocument> _all = new();

    public DocumentsViewModel(Func<LocalStore?> store, Func<Project> project, Func<bool> isSaved, Func<IDialogService?> dialogs,
        Func<string> userName, Func<string?> readOnly, Func<MarginReport> margins)
    {
        _store = store;
        _project = project;
        _isSaved = isSaved;
        _dialogs = dialogs;
        _userName = userName;
        _readOnly = readOnly;
        _margins = margins;
        foreach (var category in Enum.GetValues<DocumentCategory>())
            Categories.Add(new DocumentCategoryRow(category));
        Categories[0].IsSelected = true;
        SelectCategoryCommand = new RelayCommand(p => { if (p is DocumentCategoryRow row) Select(row.Category); });
        UploadCommand = new RelayCommand(RunUpload);
        MakeMarginsCommand = new RelayCommand(RunMakeMargins);
    }

    public ObservableCollection<DocumentCategoryRow> Categories { get; } = new();
    public ObservableCollection<DocumentRow> Documents { get; } = new();
    public ObservableCollection<TypologyRow> Typologies { get; } = new();

    public static IReadOnlyList<string> SortOptions { get; } = new[] { "Newest first", "Oldest first", "Name A–Z", "Name Z–A", "Largest first" };

    private DocumentCategory _category = DocumentCategory.PreProductionSurvey;
    public DocumentCategory Category => _category;
    public string CategoryTitle => ProjectDocument.CategoryName(_category);

    public bool IsMargins => _category == DocumentCategory.Margins;
    public bool IsTypologyHistory => _category == DocumentCategory.TypologyHistory;

    private string _search = "";
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Show(); } }

    private string _sort = SortOptions[0];
    public string Sort { get => _sort; set { if (SetProperty(ref _sort, value ?? SortOptions[0])) Show(); } }

    public bool IsEmpty => Documents.Count == 0;

    /// <summary>Why nothing can be kept yet (no database, or the quote is not saved), else null.</summary>
    public string? Unavailable => _store() is null ? "There is no local database, so documents cannot be kept."
        : !_isSaved() ? "Save the quote first: its documents are kept with it." : null;

    public bool IsAvailable => Unavailable is null;

    public ICommand SelectCategoryCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand MakeMarginsCommand { get; }

    private string? _message;
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    /// <summary>Opens a file with its program (set by the window).</summary>
    public Action<string>? OpenDocument { get; set; }

    /// <summary>Reads the quote's documents again (when the tab is shown or another quote is opened).</summary>
    public void Reload()
    {
        Message = null;
        _all = new List<ProjectDocument>();
        Typologies.Clear();
        if (IsAvailable && _store() is { } store)
        {
            try
            {
                _all = store.Documents.ForProject(_project().Id).ToList();
                LoadTypologies(store);
            }
            catch (DataStoreException ex)
            {
                Show(ex.Message, true);
            }
        }
        foreach (var row in Categories) row.Count = _all.Count(d => d.Category == row.Category);
        Categories.Single(c => c.Category == DocumentCategory.TypologyHistory).Count += Typologies.Count;
        OnPropertyChanged(nameof(Unavailable));
        OnPropertyChanged(nameof(IsAvailable));
        Show();
    }

    public void Select(DocumentCategory category)
    {
        _category = category;
        foreach (var row in Categories) row.IsSelected = row.Category == category;
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(CategoryTitle));
        OnPropertyChanged(nameof(IsMargins));
        OnPropertyChanged(nameof(IsTypologyHistory));
        Message = null;
        Show();
    }

    /// <summary>Keeps <paramref name="content"/> with the open quote (e.g. a quotation MARK just wrote). Returns an error, or null.</summary>
    public string? Keep(DocumentCategory category, string name, string fileName, byte[] content, string? addedBy = null)
    {
        if (Unavailable is { } why) return why;
        if (_readOnly() is { } readOnly) return readOnly;
        try
        {
            _store()!.Documents.Add(new ProjectDocument(Guid.NewGuid(), _project().Id, category, name.Trim(), fileName,
                DateTime.UtcNow, addedBy ?? Who(), content.LongLength), content);
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Adds a file from disk to the selected category. Returns an error, or null (also when cancelled).</summary>
    public string? Upload(string? path)
    {
        if (Unavailable is { } why) return why;
        if (_readOnly() is { } readOnly) return readOnly;
        path ??= _dialogs()?.ChooseOpenFile($"Upload to {CategoryTitle}", "All files (*.*)|*.*|PDF files (*.pdf)|*.pdf|Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (path is null) return null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > ProjectDocument.MaxSize)
                return $"'{info.Name}' is {ProjectDocument.SizeText(info.Length)}; a document can be at most {ProjectDocument.SizeText(ProjectDocument.MaxSize)}.";
            byte[] content = File.ReadAllBytes(path);
            if (Keep(_category, Path.GetFileNameWithoutExtension(path), info.Name, content) is { } error) return error;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"'{Path.GetFileName(path)}' could not be read: {ex.Message}";
        }
        Reload();
        Show($"Added {Path.GetFileName(path)} to {CategoryTitle}.", false);
        return null;
    }

    /// <summary>Writes the margins PDF of the quote as it is and keeps it under Margins. Returns an error, or null.</summary>
    public string? MakeMargins()
    {
        if (Unavailable is { } why) return why;
        if (_project().Frames.Count == 0) return "Add the designs first.";
        try
        {
            var report = _margins();
            using var stream = new MemoryStream();
            MarginPdf.Write(report, stream);
            string name = $"Margins {report.QuoteNumber} {DateTime.Now.ToString("dd-MM-yyyy HH.mm", CultureInfo.InvariantCulture)}".Trim();
            if (Keep(DocumentCategory.Margins, name, FileName(name) + ".pdf", stream.ToArray()) is { } error) return error;
        }
        catch (InvalidOperationException ex)
        {
            return $"The margin report could not be written: {ex.Message}";
        }
        Reload();
        Show("Made the margin report: it is the newest one in Margins. It is for you, not the client.", false);
        return null;
    }

    private void RunUpload()
    {
        if (Upload(null) is { } error) Show(error, true);
    }

    private void RunMakeMargins()
    {
        if (MakeMargins() is { } error) Show(error, true);
    }

    /// <summary>Saves the document to a temporary file and opens it with its program.</summary>
    public string? Open(ProjectDocument document)
    {
        try
        {
            byte[] content = _store()!.Documents.Content(document.Id);
            string folder = Path.Combine(Path.GetTempPath(), "MARK Documents", document.Id.ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName(document.FileName));
            File.WriteAllBytes(path, content);
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is DataStoreException or IOException or UnauthorizedAccessException)
        {
            return $"'{document.Name}' could not be opened: {ex.Message}";
        }
    }

    /// <summary>Saves a copy of the document where the user chooses (or to <paramref name="path"/>).</summary>
    public string? SaveCopy(ProjectDocument document, string? path = null)
    {
        string extension = Path.GetExtension(document.FileName);
        path ??= _dialogs()?.ChooseSaveFile("Save a copy", $"{(extension.Length > 0 ? extension.TrimStart('.').ToUpperInvariant() : "All")} files (*{(extension.Length > 0 ? extension : ".*")})|*{(extension.Length > 0 ? extension : ".*")}|All files (*.*)|*.*", FileName(document.FileName));
        if (path is null) return null;
        try
        {
            File.WriteAllBytes(path, _store()!.Documents.Content(document.Id));
            Show($"Saved a copy as {Path.GetFileName(path)}.", false);
            return null;
        }
        catch (Exception ex) when (ex is DataStoreException or IOException or UnauthorizedAccessException)
        {
            return $"'{document.Name}' could not be saved: {ex.Message}";
        }
    }

    public string? Delete(ProjectDocument document)
    {
        if (_readOnly() is { } readOnly) return readOnly;
        if (_dialogs() is { } dialogs && !dialogs.Confirm("Delete document", $"Delete '{document.Name}' from {ProjectDocument.CategoryName(document.Category)}? This cannot be undone."))
            return null;
        try
        {
            _store()!.Documents.Delete(document.Id);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        Reload();
        Show($"Deleted {document.Name}.", false);
        return null;
    }

    // ── Showing ─────────────────────────────────────────────────────

    private void Show()
    {
        Documents.Clear();
        string search = (_search ?? "").Trim();
        var shown = _all.Where(d => d.Category == _category)
            .Where(d => search.Length == 0 || d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                           || d.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                           || d.AddedBy.Contains(search, StringComparison.OrdinalIgnoreCase));
        shown = _sort switch
        {
            "Oldest first" => shown.OrderBy(d => d.AddedUtc),
            "Name A–Z" => shown.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            "Name Z–A" => shown.OrderByDescending(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            "Largest first" => shown.OrderByDescending(d => d.Size),
            _ => shown.OrderByDescending(d => d.AddedUtc)
        };
        foreach (var d in shown)
        {
            var document = d;
            Documents.Add(new DocumentRow(document,
                document.AddedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture),
                ProjectDocument.SizeText(document.Size),
                new RelayCommand(() => { if (Open(document) is { } e) Show(e, true); }),
                new RelayCommand(() => { if (SaveCopy(document) is { } e) Show(e, true); }),
                new RelayCommand(() => { if (Delete(document) is { } e) Show(e, true); })));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void LoadTypologies(LocalStore store)
    {
        foreach (var revision in store.Projects.Revisions(_project().Id).OrderByDescending(r => r.Revision))
        {
            string designs;
            try
            {
                var old = store.Projects.LoadRevision(revision.ProjectId, revision.Revision);
                designs = Designs(old);
            }
            catch (DataStoreException)
            {
                designs = "(could not be read)";
            }
            Typologies.Add(new TypologyRow(revision.Name,
                $"{revision.SavedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)} · {revision.SavedBy}".TrimEnd(' ', '·'),
                revision.Value is { } value ? $"{value.ToString("N2", CultureInfo.InvariantCulture)} {revision.Currency}".Trim() : "",
                designs));
        }
        if (Typologies.Count > 0)
            Typologies.Insert(0, new TypologyRow("Now", "the quote as it is", "", Designs(_project())));
    }

    /// <summary>"W1 1200 × 1500 ×2, W2 …".</summary>
    private static string Designs(Project project)
        => project.Frames.Count == 0 ? "no designs"
            : string.Join(", ", project.Frames.Select((f, i) =>
                $"{(string.IsNullOrWhiteSpace(f.Design.Reference) ? $"W{i + 1}" : f.Design.Reference)} {f.Width:0} × {f.Height:0}{(f.Design.Quantity > 1 ? $" ×{f.Design.Quantity}" : "")}"));

    private void Show(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }

    private string Who() => _userName() is { Length: > 0 } user ? user : Environment.UserName;

    private static string FileName(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
}

/// <summary>Turns a priced quote into a <see cref="MarginReport"/>.</summary>
public static class MarginBuilder
{
    private static readonly CultureInfo Indian = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>Cost lines whose name says profit or margin are the margin; everything else in the basic value is cost.</summary>
    public static bool IsMarginLine(string name)
        => name.Contains("profit", StringComparison.OrdinalIgnoreCase) || name.Contains("margin", StringComparison.OrdinalIgnoreCase);

    public static MarginReport Build(Project project, QuotePrice price, string company, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(price);
        var designs = new List<MarginDesign>();
        decimal totalCost = 0, totalPrice = 0;
        for (int n = 0; n < project.Frames.Count; n++)
        {
            var frame = project.Frames[n];
            if (price.FindDesign(frame.Id) is not { } design) continue;
            decimal unitCost = design.UnitBasicPrice - design.Heads.Where(h => IsMarginLine(h.Name)).Sum(h => h.Amount);
            decimal cost = unitCost * design.Quantity, value = design.Total;
            totalCost += cost;
            totalPrice += value;
            designs.Add(new MarginDesign(
                string.IsNullOrWhiteSpace(frame.Design.Reference) ? $"W{n + 1}" : frame.Design.Reference,
                frame.Design.Name, design.Quantity,
                design.Sheet.Select(l => new MarginLine(l.Name, l.Formula, Money(l.Amount),
                    l.Kind is CostSheetLineKind.Subtotal or CostSheetLineKind.Price)).ToList(),
                Money(cost), Money(value), Money(value - cost), Percent(value - cost, value)));
        }
        return new MarginReport
        {
            Company = company,
            QuoteNumber = project.Quote.NumberText,
            Project = project.Name,
            Client = project.Quote.Client.DisplayName,
            Date = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            Currency = price.Currency,
            Basis = "Cost is each window's basic value without the cost lines named Profit or Margin; price is after the discount, "
                    + "before charges and tax. Margin = price − cost.",
            Designs = designs,
            Totals = new[]
            {
                new QuotationRow("Cost", Money(totalCost), price.Currency),
                new QuotationRow("Price (after discount, before charges and tax)", Money(totalPrice), price.Currency),
                new QuotationRow("Margin", Money(totalPrice - totalCost), price.Currency, Bold: true),
                new QuotationRow("Margin on price", Percent(totalPrice - totalCost, totalPrice), "", Bold: true),
                new QuotationRow("Grand total quoted (with charges and tax)", Money(price.GrandTotal), price.Currency)
            }
        };
    }

    private static string Money(decimal value) => value.ToString("N2", Indian);

    private static string Percent(decimal part, decimal whole) => whole == 0 ? "—" : $"{part / whole * 100m:0.0} %";
}
