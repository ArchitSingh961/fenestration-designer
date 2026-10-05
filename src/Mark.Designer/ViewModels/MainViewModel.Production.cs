using System.Windows.Input;
using Mark.Licensing;

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
