using System.IO;
using System.Windows.Input;
using Mark.Core.Production;
using Mark.Core.Quotes;
using Mark.Licensing;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 16, production: production orders made from confirmed orders (progress per window and the workshop
/// papers) and the offcuts kept in stock.
/// </summary>
public partial class MainViewModel
{
    public ProductionViewModel Production { get; private set; } = null!;
    public OffcutsViewModel Offcuts { get; private set; } = null!;

    /// <summary>Starts (or opens) the production order of the open order and shows it.</summary>
    public ICommand StartProductionCommand { get; private set; } = null!;

    private void CreateProductionFeatures()
    {
        string? Blocked() => Access.ReadOnlyMessage
                             ?? (Access.Allows(Features.ProductionOrders) ? null : Access.Lock(Features.ProductionOrders));
        Production = new ProductionViewModel(() => Store, () => Library, () => Calculation.Rules, () => Rules, () => Dialogs,
            () => Access.CompanyName.Length > 0 ? Access.CompanyName : CompanyNameWithoutAccount())
        {
            Blocked = Blocked
        };
        Offcuts = new OffcutsViewModel(() => Store, () => Library) { Blocked = Blocked };
        StartProductionCommand = new RelayCommand(() => Report(StartProduction()), () => HasStore);
        CuttingAndLabelsCommand = new RelayCommand(() => Report(ExportCuttingAndLabels()));
    }

    /// <summary>The cutting list and piece labels of the open quote, as one PDF.</summary>
    public ICommand CuttingAndLabelsCommand { get; private set; } = null!;

    /// <summary>
    /// The open quote's cutting list (every bar drawn and listed, with the quote's bar lengths) and its piece labels, in
    /// one PDF (to <paramref name="path"/>, or where the user chooses), opened for printing; a copy is kept in the quote's
    /// Documents › Others. No production order is needed. Returns an error, or null.
    /// </summary>
    public string? ExportCuttingAndLabels(string? path = null)
    {
        if (!Access.Allows(Features.CuttingPlans)) return Access.Lock(Features.CuttingPlans);
        if (Project.Frames.Count == 0) return "Add at least one design before making the cutting list.";
        if (CommitPendingEdits() is { } pending) return pending;
        string number = Project.Quote.OrderNumber.Length > 0 ? Project.Quote.OrderNumber
            : Project.Quote.Number.Length > 0 ? Project.Quote.NumberText : "Draft";
        string fileName = string.Join("_", $"{number} {Project.Name} Cutting list and labels".Split(Path.GetInvalidFileNameChars())).Trim() + ".pdf";
        path ??= Dialogs?.ChooseSaveFile("Save the cutting list and labels", "PDF files (*.pdf)|*.pdf", fileName);
        if (path is null) return null;
        try
        {
            var order = new ProductionOrder
            {
                ProjectId = Project.Id,
                OrderNumber = number,
                QuoteNumber = Project.Quote.OrderNumber.Length > 0 && Project.Quote.Number.Length > 0 ? Project.Quote.NumberText : "",
                ProjectName = Project.Name,
                ClientName = Project.Quote.Client.DisplayName,
                DocumentJson = Core.Serialization.ProjectSerializer.Serialize(Project)
            };
            string company = Access.CompanyName.Length > 0 ? Access.CompanyName : CompanyNameWithoutAccount();
            var papers = ProductionBuilder.Build(new ProductionInputs(order, Library, Calculation.Rules, Rules,
                Array.Empty<Offcut>(), company, DateTime.UtcNow));
            using (var stream = File.Create(path))
                ProductionPdf.Write(papers.Document, ProductionSheet.CuttingListAndLabels, stream);
            if (Store?.Projects.Exists(Project.Id) == true)
                QuoteDocuments.Keep(DocumentCategory.Others, $"Cutting list and labels {number}", Path.GetFileName(path), File.ReadAllBytes(path));
            ShowNotice($"Saved {Path.GetFileName(path)}: the cutting list with every bar drawn, then the piece labels.");
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Mark.Data.DataStoreException or System.Text.Json.JsonException)
        {
            return $"The cutting list could not be written: {ex.Message}";
        }
    }

    /// <summary>The company name from Quotation setup (used without a MARK account).</summary>
    private string CompanyNameWithoutAccount()
    {
        try
        {
            return Store?.Settings.LoadQuotationSettings().CompanyName ?? "";
        }
        catch (Mark.Data.DataStoreException)
        {
            return "";
        }
    }

    /// <summary>
    /// The open quote, an order, goes into production (or its production order is opened): Production › Production
    /// orders. Returns an error, or null.
    /// </summary>
    public string? StartProduction()
    {
        if (Store is null) return "There is no local database.";
        if (!Access.Allows(Features.ProductionOrders)) return Access.Lock(Features.ProductionOrders);
        if (!IsOrder) return "Convert the quote to an order first.";
        if (IsDirty) return "Save the order first: production starts from the saved designs.";
        Production.OpenDocument = OpenDocument;
        string? error;
        try
        {
            error = Store.Production.ForProject(Project.Id) is { } existing ? null : Production.Create(Project.Id);
            ShowView(AppView.ProductionOrders, AppArea.Production);
            if (Store.Production.ForProject(Project.Id) is { } id) Production.Reload(id);
        }
        catch (Mark.Data.DataStoreException ex)
        {
            error = ex.Message;
        }
        return error;
    }
}
