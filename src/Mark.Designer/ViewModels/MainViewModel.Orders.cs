using System.Windows.Input;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 17, orders: every order from confirmation to installation — stage, payments, delivery and installation
/// schedule, dispatch notes and installation sign-off.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Orders › Orders and Schedule.</summary>
    public OrdersViewModel OrderBook { get; private set; } = null!;

    /// <summary>Opens the open order's page in Orders.</summary>
    public ICommand ShowOrderCommand { get; private set; } = null!;

    private void CreateOrderFeatures()
    {
        OrderBook = new OrdersViewModel(() => Store, () => Dialogs, Letterhead, () => Access.UserName)
        {
            Blocked = () => Access.ReadOnlyMessage
                            ?? (Access.Allows(Features.OrderManagement) ? null : Access.Lock(Features.OrderManagement)),
            OpenQuote = id => Report(OpenOrderQuote(id)),
            OpenProduction = id => Report(OpenOrderProduction(id)),
            ShowOrdersTab = () => ShowView(AppView.Orders, AppArea.Orders)
        };
        ShowOrderCommand = new RelayCommand(() => Report(ShowOrder()), () => HasStore);
    }

    // ── Changing an order ───────────────────────────────────────────

    /// <summary>The order whose changes the user agreed to save (asked once per opened order).</summary>
    private Guid? _orderChangeAgreed;

    /// <summary>The open order has a production order: the workshop makes what was saved.</summary>
    private bool OrderIsInProduction()
    {
        try
        {
            return IsOrder && Store?.Production.ForProject(Project.Id) is not null;
        }
        catch (DataStoreException)
        {
            return false;
        }
    }

    /// <summary>The first change to an order says so at once: it changes what the client ordered (and what is made).</summary>
    private void WarnOnOrderChange()
    {
        if (!IsOrder) return;
        ShowNotice(OrderIsInProduction()
            ? $"This is order {Project.Quote.OrderNumber}, in production: the workshop makes what was saved. Undo (Ctrl+Z) to leave the order as it is."
            : $"This is order {Project.Quote.OrderNumber}: a change changes what the client ordered. Undo (Ctrl+Z) to leave the order as it is.");
    }

    /// <summary>
    /// Saving a change to an order asks first (once per opened order). True when the user said no: nothing is saved.
    /// </summary>
    private bool OrderChangeRefused()
    {
        if (!IsOrder || !IsDirty || Dialogs is null || _orderChangeAgreed == Project.Id) return false;
        bool inProduction = OrderIsInProduction();
        string question = inProduction
            ? $"{Project.Quote.OrderNumber} is in production. Save the changes to the order anyway?\n\nThe production order " +
              "was made from the saved designs: its cutting list, glass order and labels do not change by themselves. Tell the workshop."
            : $"{Project.Quote.OrderNumber} is an order. Save the changes to it anyway?\n\nThey change what the client ordered.";
        if (!Dialogs.Confirm("Change an order", question)) return true;
        _orderChangeAgreed = Project.Id;
        return false;
    }

    /// <summary>The open quote's order in Orders › Orders (the quote must be an order, and saved). Returns an error, or null.</summary>
    public string? ShowOrder()
    {
        if (Store is null) return "There is no local database.";
        if (!Access.Allows(Features.OrderManagement)) return Access.Lock(Features.OrderManagement);
        if (!IsOrder) return "Convert the quote to an order first.";
        if (IsDirty) return "Save the order first.";
        OrderBook.OpenDocument = OpenDocument;
        ShowView(AppView.Orders, AppArea.Orders);
        return OrderBook.ShowProject(Project.Id);
    }

    private string? OpenOrderQuote(Guid projectId)
    {
        if (Project.Id != projectId)
        {
            if (!ConfirmDiscardChanges()) return null;
            if (OpenProject(projectId) is { } error) return error;
        }
        ShowView(AppView.Client, AppArea.Sales);
        return null;
    }

    private string? OpenOrderProduction(Guid projectId)
    {
        if (Project.Id != projectId)
        {
            if (!ConfirmDiscardChanges()) return null;
            if (OpenProject(projectId) is { } error) return error;
        }
        return StartProduction();
    }

    /// <summary>The company's name, address and phone for order papers (as on the quotation).</summary>
    private Letterhead Letterhead()
    {
        QuotationSettings settings;
        try
        {
            settings = Store?.Settings.LoadQuotationSettings() ?? new QuotationSettings();
        }
        catch (DataStoreException)
        {
            settings = new QuotationSettings();
        }
        if (CompanyProfile() is { } profile) settings = QuotationBuilder.WithProfile(settings, profile);
        string company = Access.IsLicensed && Access.CompanyName.Length > 0 ? Access.CompanyName
            : settings.CompanyName.Length > 0 ? settings.CompanyName : "Your company";
        return new Letterhead(company, settings.Address.Replace("\r", "").Replace("\n", ", ").Trim(), settings.Phone);
    }
}
