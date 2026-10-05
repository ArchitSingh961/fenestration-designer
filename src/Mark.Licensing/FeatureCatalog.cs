namespace Mark.Licensing;

/// <summary>
/// One thing MARK can do that the owner can include in, or leave out of, a client's package.
/// </summary>
/// <param name="Id">Stable identifier stored in packages and licences, e.g. "pricing.priceStructure". Never reused.</param>
/// <param name="Area">The area of MARK it belongs to (Sales, Design, Pricing…).</param>
/// <param name="IsBuilt">False for features of later milestones: they can already be put in packages, and switch on
/// when that part of MARK is released.</param>
/// <param name="IsCore">Always included: every account has it, and it cannot be removed.</param>
public sealed record Feature(string Id, string Area, string Name, string Description, bool IsBuilt = true, bool IsCore = false);

/// <summary>Feature identifiers used in code (gates in MARK). The full list with names is <see cref="FeatureCatalog"/>.</summary>
public static class Features
{
    public const string Quotes = "sales.quotes";
    public const string Enquiries = "sales.enquiries";
    public const string QuotationPdf = "sales.quotationPdf";
    public const string SalesCharts = "sales.charts";
    public const string Drawing = "design.drawing";
    public const string Openings = "design.openings";
    public const string DesignLibrary = "design.library";
    public const string ProjectFiles = "design.projectFiles";
    public const string Costing = "pricing.costing";
    public const string PriceStructure = "pricing.priceStructure";
    public const string LibraryManager = "library.manage";
    public const string CuttingPlans = "production.cutting";
    public const string ProductionOrders = "production.orders";
    public const string OrderManagement = "orders.manage";
    public const string Purchasing = "purchasing.orders";
    public const string Inventory = "inventory.stock";
    public const string Invoices = "accounts.invoices";
}

/// <summary>Everything in MARK that can be sold, grouped by area. Packages and add-ons are made from these.</summary>
public static class FeatureCatalog
{
    public static IReadOnlyList<string> Areas { get; } = new[]
    {
        "Sales", "Design", "Pricing", "Library", "Production", "Orders", "Purchasing", "Inventory", "Accounts"
    };

    public static IReadOnlyList<Feature> All { get; } = new[]
    {
        new Feature(Features.Quotes, "Sales", "Quotes and clients", "Dashboard, quote list, client details and design cards.", IsCore: true),
        new Feature(Features.Enquiries, "Sales", "Enquiries", "Enquiry form and enquiry → quote → order."),
        new Feature(Features.QuotationPdf, "Sales", "Quotation PDF", "Quotations with the company logo, drawings and terms."),
        new Feature(Features.SalesCharts, "Sales", "Sales charts", "Quotes won and lost by week, month, person and city."),
        new Feature(Features.Drawing, "Design", "Frame designer", "Draw frames, mullions and transoms to exact sizes.", IsCore: true),
        new Feature(Features.Openings, "Design", "Openings", "Casement, tilt & turn, sliding, pivot and mesh shutters."),
        new Feature(Features.DesignLibrary, "Design", "Design library", "Ready-made designs applied by click or drag."),
        new Feature(Features.ProjectFiles, "Design", "Project files", "Import and export quotes as files."),
        new Feature(Features.Costing, "Pricing", "Bill of materials and cost", "Profiles, glass and hardware with their cost."),
        new Feature(Features.PriceStructure, "Pricing", "Price structure", "Cost lines, rates, discount, charges and GST per quote."),
        new Feature(Features.LibraryManager, "Library", "Library Manager", "Add and edit profiles, glass and hardware."),
        new Feature(Features.CuttingPlans, "Production", "Cutting plans", "Stock bars, cut lengths, offcuts and waste."),
        new Feature(Features.ProductionOrders, "Production", "Production orders",
            "Production orders from confirmed orders: cutting lists with offcuts, glass orders, hardware pick lists, shop drawings, labels and progress."),
        new Feature(Features.OrderManagement, "Orders", "Order management",
            "Orders from confirmation to installation: stage, advance and stage payments, delivery and installation schedule, dispatch notes and installation sign-off."),
        new Feature(Features.Purchasing, "Purchasing", "Purchasing",
            "Suppliers; purchase orders worked out from what orders need minus stock and what is on order; goods received into stock."),
        new Feature(Features.Inventory, "Inventory", "Inventory",
            "Stock of bars, glass and hardware: reserved for orders, issued to production, counted and adjusted, low-stock alerts."),
        new Feature(Features.Invoices, "Accounts", "Invoices and payments",
            "GST tax invoices from orders, payment receipts, what each client owes, and export to Excel and Tally.")
    };

    private static readonly Dictionary<string, Feature> ById = All.ToDictionary(f => f.Id);

    public static Feature? Find(string id) => ById.GetValueOrDefault(id);

    public static bool Exists(string id) => ById.ContainsKey(id);

    /// <summary>Features every account has.</summary>
    public static IEnumerable<string> CoreIds => All.Where(f => f.IsCore).Select(f => f.Id);

    /// <summary>The name of a feature, or its id if it is not (or no longer) in the catalogue.</summary>
    public static string NameOf(string id) => Find(id)?.Name ?? id;
}

/// <summary>The packages a new licence server starts with; the owner can change or delete them.</summary>
public static class StarterPackages
{
    public static IReadOnlyList<(string Name, string Description, IReadOnlyList<string> Features)> All { get; } = new[]
    {
        ("Basic", "Quotes and designing with openings, ready-made designs and cost.",
            (IReadOnlyList<string>)new[] { Features.Quotes, Features.Drawing, Features.Openings, Features.DesignLibrary, Features.Costing }),
        ("Professional", "Basic plus enquiries, quotation PDF, price structure, Library Manager, cutting plans and project files.",
            new[]
            {
                Features.Quotes, Features.Drawing, Features.Openings, Features.DesignLibrary, Features.Costing,
                Features.PriceStructure, Features.LibraryManager, Features.CuttingPlans, Features.ProjectFiles,
                Features.Enquiries, Features.QuotationPdf
            }),
        ("Complete", "Everything in MARK, including each new area as it is released.",
            FeatureCatalog.All.Select(f => f.Id).ToList())
    };
}
