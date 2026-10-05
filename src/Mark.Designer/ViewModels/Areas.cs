using System.Windows.Input;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>The areas of MARK, in the order of the area bar (the same areas as <see cref="FeatureCatalog.Areas"/>).</summary>
public enum AppArea { Sales, Design, Pricing, Library, Production, Orders, Purchasing, Inventory, Accounts }

/// <summary>Everything MARK can show, as the tabs of the areas (one view can be a tab of more than one area).</summary>
public enum AppView { Dashboard, Quotes, Client, Designs, Drawing, Pricing, Materials, Cutting, Library, Overview, Account, Staff, Enquiries, QuotationSetup, ProductionOrders, Offcuts, Orders, Schedule, Documents }

/// <summary>How a tab or area looks to the signed-in login.</summary>
public enum AccessState
{
    /// <summary>Usable.</summary>
    Open,

    /// <summary>Not in the company's package: shown with a lock and a message (the account owner sees what can be added).</summary>
    Locked,

    /// <summary>Not given to this staff login: not shown at all.</summary>
    Hidden
}

/// <summary>A tab of an area: the view it shows and the feature it needs (null: the area's overview).</summary>
public sealed record AreaTabInfo(AppView View, string Title, string? FeatureId);

/// <summary>An area: its name, icon, short description and tabs.</summary>
public sealed record AreaInfo(AppArea Area, string Name, string Glyph, string Description, IReadOnlyList<AreaTabInfo> Tabs)
{
    /// <summary>True when the area has no part of MARK yet (it shows what is coming).</summary>
    public bool IsComingLater => Tabs.All(t => t.View == AppView.Overview);

    /// <summary>The features of this area in the feature catalogue (packages and staff logins are made of them).</summary>
    public IEnumerable<Feature> Features => FeatureCatalog.All.Where(f => f.Area == Name);
}

/// <summary>The areas, their tabs and the feature each tab needs.</summary>
public static class AreaCatalog
{
    public static IReadOnlyList<AreaInfo> All { get; } = new[]
    {
        new AreaInfo(AppArea.Sales, "Sales", "", "Dashboard, enquiries, quotes, clients and quotations.", new[]
        {
            new AreaTabInfo(AppView.Dashboard, "Dashboard", Licensing.Features.Quotes),
            new AreaTabInfo(AppView.Enquiries, "Enquiries", Licensing.Features.Enquiries),
            new AreaTabInfo(AppView.Quotes, "Quotes", Licensing.Features.Quotes),
            new AreaTabInfo(AppView.Client, "Client", Licensing.Features.Quotes),
            new AreaTabInfo(AppView.Designs, "Designs", Licensing.Features.Quotes),
            new AreaTabInfo(AppView.Documents, "Documents", Licensing.Features.Quotes),
            new AreaTabInfo(AppView.QuotationSetup, "Quotation setup", Licensing.Features.QuotationPdf)
        }),
        new AreaInfo(AppArea.Design, "Design", "", "Windows and doors drawn to size, with openings and ready-made designs.", new[]
        {
            new AreaTabInfo(AppView.Designs, "Designs", Licensing.Features.Drawing),
            new AreaTabInfo(AppView.Drawing, "Drawing", Licensing.Features.Drawing)
        }),
        new AreaInfo(AppArea.Pricing, "Pricing", "", "The quote's price structure and its bill of materials.", new[]
        {
            new AreaTabInfo(AppView.Pricing, "Price", Licensing.Features.PriceStructure),
            new AreaTabInfo(AppView.Materials, "Bill of materials", Licensing.Features.Costing)
        }),
        new AreaInfo(AppArea.Library, "Library", "", "Systems, profiles, glass, hardware and their prices.", new[]
        {
            new AreaTabInfo(AppView.Library, "Library", Licensing.Features.LibraryManager)
        }),
        new AreaInfo(AppArea.Production, "Production", "", "Production orders, cutting plans and offcuts in stock.", new[]
        {
            new AreaTabInfo(AppView.ProductionOrders, "Production orders", Licensing.Features.ProductionOrders),
            new AreaTabInfo(AppView.Cutting, "Cutting plan", Licensing.Features.CuttingPlans),
            new AreaTabInfo(AppView.Offcuts, "Offcuts", Licensing.Features.ProductionOrders)
        }),
        new AreaInfo(AppArea.Orders, "Orders", "", "Orders from confirmation to installation: payments, dispatch and sign-off.", new[]
        {
            new AreaTabInfo(AppView.Orders, "Orders", Licensing.Features.OrderManagement),
            new AreaTabInfo(AppView.Schedule, "Schedule", Licensing.Features.OrderManagement)
        }),
        new AreaInfo(AppArea.Purchasing, "Purchasing", "", "Suppliers, purchase orders and goods received.", Overview()),
        new AreaInfo(AppArea.Inventory, "Inventory", "", "Stock of bars, glass and hardware.", Overview()),
        new AreaInfo(AppArea.Accounts, "Accounts", "", "Invoices, payments and Tally export.", Overview())
    };

    private static AreaTabInfo[] Overview() => new[] { new AreaTabInfo(AppView.Overview, "Overview", null) };

    public static AreaInfo Of(AppArea area) => All[(int)area];

    /// <summary>The area a view belongs to first (Designs is in Sales and Design).</summary>
    public static AppArea PrimaryAreaOf(AppView view)
        => All.FirstOrDefault(a => a.Tabs.Any(t => t.View == view))?.Area ?? AppArea.Sales;
}

/// <summary>A tab in the header for the current area.</summary>
public sealed class AreaTab : ViewModelBase
{
    public AreaTab(AreaTabInfo info, AccessState state, ICommand show)
    {
        Info = info;
        State = state;
        ShowCommand = show;
    }

    public AreaTabInfo Info { get; }

    public AppView View => Info.View;

    public string Title => Info.Title;

    public override string ToString() => Title;

    public AccessState State { get; }

    public bool IsLocked => State == AccessState.Locked;

    public ICommand ShowCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>An area in the area bar.</summary>
public sealed class AreaItem : ViewModelBase
{
    public AreaItem(AreaInfo info, ICommand show)
    {
        Info = info;
        ShowCommand = show;
    }

    public AreaInfo Info { get; }

    public AppArea Area => Info.Area;

    public string Name => Info.Name;

    public string Glyph => Info.Glyph;

    public override string ToString() => Name;

    public ICommand ShowCommand { get; }

    private AccessState _state;
    public AccessState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(IsLocked));
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    public bool IsVisible => _state != AccessState.Hidden;

    public bool IsLocked => _state == AccessState.Locked;

    public bool IsComingLater => Info.IsComingLater;

    public string ToolTip => Info.IsComingLater ? $"{Name}: coming in a later version of MARK"
        : IsLocked ? $"{Name}: not in your MARK package" : $"{Name}: {Info.Description}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>A feature of an area on its overview page: "Order management — Coming in a later version · in your package".</summary>
public sealed record AreaFeatureRow(string Name, string Description, string State);
