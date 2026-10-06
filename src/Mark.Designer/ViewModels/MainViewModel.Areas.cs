using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Core.Library;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>A product system on the Library page: "62mm Casement – uPVC · Windows".</summary>
public sealed record LibrarySystemRow(string Name, string Detail);

/// <summary>
/// Milestone 14: MARK in areas (Sales, Design, Pricing, Library, Production, Orders, Purchasing, Inventory, Accounts).
/// The area bar shows every area the login may see; the header shows the tabs of the chosen area. Features outside the
/// company's package are shown locked; for a staff login, what its account owner did not give it is not shown, and
/// cannot be reached: any page it may not see sends it to its first area.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The area bar.</summary>
    public ObservableCollection<AreaItem> Areas { get; } = new();

    /// <summary>The tabs of the current area, in the header.</summary>
    public ObservableCollection<AreaTab> Tabs { get; } = new();

    public ICommand ShowAreaCommand { get; private set; } = null!;
    public ICommand ShowViewCommand { get; private set; } = null!;

    private AppArea _area = AppArea.Design;
    private readonly Dictionary<AppArea, AppView> _lastView = new();
    private bool _navigating;

    private void CreateAreaFeatures()
    {
        ShowAreaCommand = new RelayCommand(p =>
        {
            if (p is AppArea area) ShowArea(area);
            else if (p is AreaItem item) ShowArea(item.Area);
            else if (p is string name && Enum.TryParse(name, out AppArea parsed)) ShowArea(parsed);
        });
        ShowViewCommand = new RelayCommand(p =>
        {
            if (p is AppView view) ShowView(view);
            else if (p is AreaTab tab) ShowView(tab.View);
            else if (p is string name && Enum.TryParse(name, out AppView parsed)) ShowView(parsed);
        });
        foreach (var info in AreaCatalog.All)
            Areas.Add(new AreaItem(info, ShowAreaCommand));
        RefreshNavigation();
    }

    /// <summary>The area shown in the area bar and whose tabs are in the header.</summary>
    public AppArea Area => _area;

    public AreaInfo AreaInfo => AreaCatalog.Of(_area);

    /// <summary>What is shown now, from <see cref="Page"/> and <see cref="Section"/>.</summary>
    public AppView CurrentView => _page switch
    {
        AppPage.Dashboard => AppView.Dashboard,
        AppPage.Quotes => AppView.Quotes,
        AppPage.Account => AppView.Account,
        AppPage.Staff => AppView.Staff,
        AppPage.Library => AppView.Library,
        AppPage.Overview => AppView.Overview,
        AppPage.Enquiries => AppView.Enquiries,
        AppPage.QuotationSetup => AppView.QuotationSetup,
        AppPage.ProductionOrders => AppView.ProductionOrders,
        AppPage.Offcuts => AppView.Offcuts,
        AppPage.Orders => AppView.Orders,
        AppPage.Schedule => AppView.Schedule,
        AppPage.Stock => AppView.Stock,
        AppPage.PurchaseOrders => AppView.PurchaseOrders,
        AppPage.Suppliers => AppView.Suppliers,
        AppPage.Invoices => AppView.Invoices,
        AppPage.Receipts => AppView.Receipts,
        AppPage.Outstanding => AppView.Outstanding,
        AppPage.AccountsExport => AppView.AccountsExport,
        _ => _section switch
        {
            QuoteSection.Client => AppView.Client,
            QuoteSection.Designs => AppView.Designs,
            QuoteSection.Pricing => AppView.Pricing,
            QuoteSection.Materials => AppView.Materials,
            QuoteSection.Cutting => AppView.Cutting,
            QuoteSection.Documents => AppView.Documents,
            QuoteSection.Products => AppView.Products,
            _ => AppView.Drawing
        }
    };

    /// <summary>The header title: the area's name, or "Account" / "Staff logins" on those pages (which have no tabs).</summary>
    public string HeaderTitle => CurrentView switch
    {
        AppView.Account => "Account",
        AppView.Staff => "Staff logins",
        _ => AreaInfo.Name
    };

    /// <summary>The area's tabs are shown (not on the Account and Staff pages).</summary>
    public bool ShowsTabs => CurrentView is not (AppView.Account or AppView.Staff);

    /// <summary>True for the tabs that show the open quote (with its header and "Open quote…").</summary>
    public bool IsQuoteView => _page == AppPage.Quote;

    // ── What the login may see ──────────────────────────────────────

    /// <summary>How a tab looks to the signed-in login.</summary>
    public AccessState TabState(AreaTabInfo tab, AppArea area)
    {
        if (tab.FeatureId is null) return OverviewState(area);
        if (tab.View == AppView.Library)
            return Access.CanUseLibrary ? AccessState.Open : Access.IsStaff ? AccessState.Hidden : AccessState.Locked;
        if (Access.Allows(tab.FeatureId)) return AccessState.Open;
        return Access.IsWithheld(tab.FeatureId) ? AccessState.Hidden : AccessState.Locked;
    }

    /// <summary>An area still to come: the account owner sees what is coming; staff only areas they were given part of.</summary>
    private AccessState OverviewState(AppArea area)
        => !Access.IsStaff || AreaCatalog.Of(area).Features.Any(f => Access.IsGiven(f.Id)) ? AccessState.Open : AccessState.Hidden;

    /// <summary>Open when any tab is open; locked when none is open but some are locked; otherwise hidden.</summary>
    public AccessState AreaState(AppArea area)
    {
        var states = AreaCatalog.Of(area).Tabs.Select(t => TabState(t, area)).ToList();
        return states.Contains(AccessState.Open) ? AccessState.Open
            : states.Contains(AccessState.Locked) ? AccessState.Locked
            : AccessState.Hidden;
    }

    /// <summary>True when the login may see the view in the area (locked views are shown, with what is missing).</summary>
    public bool CanShow(AppView view, AppArea area)
    {
        if (view == AppView.Account) return true;
        if (view == AppView.Staff) return Access.CanManageStaff && Staff is not null;
        var tab = AreaCatalog.Of(area).Tabs.FirstOrDefault(t => t.View == view);
        return tab is not null && TabState(tab, area) != AccessState.Hidden;
    }

    /// <summary>The area a view is shown in: the current one if it has the view, otherwise the first that may show it.</summary>
    private AppArea AreaFor(AppView view)
    {
        if (view is AppView.Account or AppView.Staff || CanShow(view, _area)) return _area;
        return AreaCatalog.All.FirstOrDefault(a => CanShow(view, a.Area))?.Area ?? AreaCatalog.PrimaryAreaOf(view);
    }

    /// <summary>Where the login starts: the first tab it may use of its first area (the account page if it has none).</summary>
    public (AppView View, AppArea Area) HomeView()
    {
        foreach (var area in AreaCatalog.All)
        {
            var tabs = area.Tabs.Select(t => (Tab: t, State: TabState(t, area.Area))).Where(t => t.State != AccessState.Hidden).ToList();
            if (tabs.Count == 0 || area.IsComingLater) continue;
            var tab = tabs.FirstOrDefault(t => t.State == AccessState.Open).Tab ?? tabs[0].Tab;
            return (tab.View, area.Area);
        }
        return (AppView.Account, _area);
    }

    // ── Navigation ──────────────────────────────────────────────────

    /// <summary>Shows an area at the tab last used there (or its first usable tab).</summary>
    public void ShowArea(AppArea area)
    {
        if (AreaState(area) == AccessState.Hidden) return;
        var info = AreaCatalog.Of(area);
        if (_lastView.TryGetValue(area, out var last) && CanShow(last, area))
        {
            ShowView(last, area);
            return;
        }
        var first = info.Tabs.FirstOrDefault(t => TabState(t, area) == AccessState.Open) ?? info.Tabs.First(t => TabState(t, area) != AccessState.Hidden);
        ShowView(first.View, area);
    }

    /// <summary>Shows a view (in <paramref name="area"/>, or the area that has it). Views the login may not see are refused.</summary>
    public void ShowView(AppView view, AppArea? area = null)
    {
        var target = area ?? AreaFor(view);
        if (!CanShow(view, target)) return;
        _navigating = true;
        try
        {
            _area = target;
            switch (view)
            {
                case AppView.Dashboard: Page = AppPage.Dashboard; break;
                case AppView.Quotes: Page = AppPage.Quotes; break;
                case AppView.Account: Page = AppPage.Account; break;
                case AppView.Staff: Page = AppPage.Staff; break;
                case AppView.Library: Page = AppPage.Library; break;
                case AppView.Overview: Page = AppPage.Overview; break;
                case AppView.Enquiries: Page = AppPage.Enquiries; break;
                case AppView.QuotationSetup: Page = AppPage.QuotationSetup; break;
                case AppView.ProductionOrders: Page = AppPage.ProductionOrders; break;
                case AppView.Offcuts: Page = AppPage.Offcuts; break;
                case AppView.Orders: Page = AppPage.Orders; break;
                case AppView.Schedule: Page = AppPage.Schedule; break;
                case AppView.Stock: Page = AppPage.Stock; break;
                case AppView.PurchaseOrders: Page = AppPage.PurchaseOrders; break;
                case AppView.Suppliers: Page = AppPage.Suppliers; break;
                case AppView.Invoices: Page = AppPage.Invoices; break;
                case AppView.Receipts: Page = AppPage.Receipts; break;
                case AppView.Outstanding: Page = AppPage.Outstanding; break;
                case AppView.AccountsExport: Page = AppPage.AccountsExport; break;
                default:
                    Section = view switch
                    {
                        AppView.Client => QuoteSection.Client,
                        AppView.Designs => QuoteSection.Designs,
                        AppView.Pricing => QuoteSection.Pricing,
                        AppView.Materials => QuoteSection.Materials,
                        AppView.Cutting => QuoteSection.Cutting,
                        AppView.Documents => QuoteSection.Documents,
                        AppView.Products => QuoteSection.Products,
                        _ => QuoteSection.Drawing
                    };
                    Page = AppPage.Quote;
                    break;
            }
        }
        finally
        {
            _navigating = false;
        }
        OnViewChanged();
    }

    /// <summary>
    /// After the page or tab changed (from navigation or from code, e.g. opening a quote): keeps the area in step, sends
    /// the login home from a view it may not see, and updates the area bar and tabs.
    /// </summary>
    private void OnViewChanged()
    {
        if (_navigating || ShowViewCommand is null) return;
        var view = CurrentView;
        var area = AreaFor(view);
        if (!CanShow(view, area))
        {
            var (home, homeArea) = HomeView();
            if (home != view || homeArea != _area)
            {
                ShowView(home, homeArea);
                return;
            }
        }
        if (view is not (AppView.Account or AppView.Staff))
        {
            _area = area;
            _lastView[area] = view;
        }
        ClearNoticeOfOtherView();
        RefreshNavigation();
    }

    /// <summary>Area states, the current area's tabs and which is selected (after a change of view or of the licence).</summary>
    private void RefreshNavigation()
    {
        if (ShowViewCommand is null) return;                     // still being constructed
        var view = CurrentView;
        bool inArea = view is not (AppView.Account or AppView.Staff);
        foreach (var item in Areas)
        {
            item.State = AreaState(item.Area);
            item.IsSelected = inArea && item.Area == _area;
        }

        var info = AreaCatalog.Of(_area);
        var wanted = info.Tabs.Select(t => (Tab: t, State: TabState(t, _area))).Where(t => t.State != AccessState.Hidden).ToList();
        bool same = Tabs.Count == wanted.Count && Tabs.Zip(wanted).All(p => p.First.Info == p.Second.Tab && p.First.State == p.Second.State);
        if (!same)
        {
            Tabs.Clear();
            foreach (var (tab, state) in wanted)
                Tabs.Add(new AreaTab(tab, state, ShowViewCommand));
        }
        foreach (var tab in Tabs)
            tab.IsSelected = inArea && tab.View == view;

        OnPropertyChanged(nameof(Area));
        OnPropertyChanged(nameof(AreaInfo));
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(IsQuoteView));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ShowsTabs));
        if (view == AppView.Overview) RefreshOverview();
        if (view == AppView.Library) RefreshLibraryPage();
    }

    // ── Overview of an area still to come ──────────────────────────

    public IReadOnlyList<AreaFeatureRow> OverviewFeatures { get; private set; } = Array.Empty<AreaFeatureRow>();

    private void RefreshOverview()
    {
        OverviewFeatures = AreaInfo.Features.Select(f => new AreaFeatureRow(f.Name, f.Description,
            (f.IsBuilt ? "Available" : "Coming in a later version of MARK") + ForYou(f.Id))).ToList();
        OnPropertyChanged(nameof(OverviewFeatures));
    }

    /// <summary>" · included for you", " · not in your package" or " · not part of your login" ("" without a licence).</summary>
    private string ForYou(string featureId)
    {
        if (Access.Status is not { } status) return "";
        bool inPackage = status.Licence.Features.Any(g => g.FeatureId == featureId);
        if (inPackage && Access.IsGiven(featureId)) return " · included for you";
        return Access.IsStaff ? " · not part of your login" : " · not in your package";
    }

    // ── Library page ────────────────────────────────────────────────

    public string LibraryCountsText { get; private set; } = "";

    public IReadOnlyList<LibrarySystemRow> LibrarySystems { get; private set; } = Array.Empty<LibrarySystemRow>();

    /// <summary>"Your library follows your MARK supplier's catalogue: you set your own prices." or the full-edit text.</summary>
    public string LibraryModeText => Access.IsCatalogueManaged
        ? "Your products come from your MARK supplier's catalogue. You set your own prices in the Library Manager; everything else is kept up to date for you."
        : "Add and change systems, profiles, glass, hardware, bundles and prices in the Library Manager.";

    private void RefreshLibraryPage()
    {
        int profiles = Library.Profiles.Count(p => p.IsActive);
        int glass = Library.Glass.Count(g => g.IsActive);
        int materials = Library.Materials.Count(m => m.IsActive);
        int systems = Library.Systems.Count(s => s.IsActive);
        LibraryCountsText = string.Format(CultureInfo.InvariantCulture,
            "{0} system{1} · {2} profile{3} · {4} glass type{5} · {6} hardware and accessor{7} · {8} bundle{9}",
            systems, systems == 1 ? "" : "s", profiles, profiles == 1 ? "" : "s", glass, glass == 1 ? "" : "s",
            materials, materials == 1 ? "y" : "ies", Library.Bundles.Count, Library.Bundles.Count == 1 ? "" : "s");
        LibrarySystems = Library.Systems.Where(s => s.IsActive).Select(s => new LibrarySystemRow(s.Name,
            $"{(s.Material == SystemMaterial.Upvc ? "uPVC" : "Aluminium")} · {UseText(s.Use)}" +
            (s.Id == Library.DefaultSystem?.Id ? " · used for new windows" : ""))).ToList();
        OnPropertyChanged(nameof(LibraryCountsText));
        OnPropertyChanged(nameof(LibrarySystems));
        OnPropertyChanged(nameof(LibraryModeText));
    }

    private static string UseText(ProductUse use) => use switch
    {
        ProductUse.Window => "Windows",
        ProductUse.Door => "Doors",
        _ => "Windows and doors"
    };
}
