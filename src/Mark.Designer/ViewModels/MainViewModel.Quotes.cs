using System.Globalization;
using System.Windows.Input;
using Mark.Core.Commands;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>The application's main pages (Milestone 14: grouped into areas, see <see cref="AppArea"/>).</summary>
public enum AppPage { Dashboard, Quotes, Quote, Account, Library, Overview, Staff, Enquiries, QuotationSetup, ProductionOrders, Offcuts, Orders, Schedule }

/// <summary>The parts of the open quote: Client, Designs (Sales); Drawing (Design); Pricing, Materials (Pricing); Cutting (Production).</summary>
public enum QuoteSection { Client, Designs, Drawing, Pricing, Materials, Cutting, Documents }

/// <summary>
/// Milestone 10: the open project is a quote. Navigation (Dashboard, Quotes, the open quote with its Client, Designs
/// and Drawing tabs), client details, design cards, the quote list and the dashboard. Every change to the quote goes
/// through the command history like design edits; Save stores the quote with its number and value.
/// </summary>
public partial class MainViewModel
{
    public QuoteDetailsViewModel Details { get; private set; } = null!;
    public DesignListViewModel Designs { get; private set; } = null!;
    public QuoteListViewModel Quotes { get; private set; } = null!;
    public DashboardViewModel Dashboard { get; private set; } = null!;

    public ICommand ShowPageCommand { get; private set; } = null!;
    public ICommand ShowSectionCommand { get; private set; } = null!;

    private void CreateQuoteFeatures()
    {
        Details = new QuoteDetailsViewModel(SetQuoteDetails);
        Designs = new DesignListViewModel(() => Project, () => Calculation.Result, () => Price, () => Library, Rules,
            EditDesign, DuplicateDesign, DeleteDesign, NewDesign);
        Quotes = new QuoteListViewModel(() => Store?.Projects, () => Project.Id, () => Dialogs, OpenQuote, NewQuote);
        Dashboard = new DashboardViewModel(() => Store?.Projects, OpenQuote, NewQuote);
        ShowPageCommand = new RelayCommand(p =>
        {
            if (p is AppPage page) Page = page;
            else if (p is string name && Enum.TryParse(name, out AppPage parsed)) Page = parsed;
        });
        ShowSectionCommand = new RelayCommand(p =>
        {
            if (p is QuoteSection section) Section = section;
            else if (p is string name && Enum.TryParse(name, out QuoteSection parsed)) Section = parsed;
        });
    }

    // ── Navigation ──────────────────────────────────────────────────

    private AppPage _page = AppPage.Quote;

    /// <summary>The page shown. Showing the dashboard or the quote list reads the saved quotes again.</summary>
    public AppPage Page
    {
        get => _page;
        set
        {
            if (_page != value)
            {
                ActiveTool.Cancel();
                Interaction.Clear();
                if (_page == AppPage.QuotationSetup) QuotationSetup.SaveIfChanged();     // what was typed there is kept
            }
            _page = value;
            OnPropertyChanged();
            if (value == AppPage.Dashboard)
            {
                Dashboard.Reload();
                SalesCharts.Reload();
            }
            if (value == AppPage.Enquiries) Enquiries.Reload();
            if (value == AppPage.QuotationSetup) QuotationSetup.Load();
            if (value == AppPage.ProductionOrders)
            {
                Production.OpenDocument = OpenDocument;
                Production.Reload();
            }
            if (value == AppPage.Offcuts) Offcuts.Reload();
            if (value is AppPage.Orders or AppPage.Schedule)
            {
                OrderBook.OpenDocument = OpenDocument;
                OrderBook.Reload();
            }
            if (value == AppPage.Quotes) Quotes.Reload();
            if (value == AppPage.Account) Account?.Refresh();
            if (value == AppPage.Staff) Staff?.Reload();
            Designs.IsVisible = value == AppPage.Quote && _section == QuoteSection.Designs;
            RefreshDocumentsIfShown();
            OnViewChanged();
        }
    }

    private QuoteSection _section = QuoteSection.Drawing;

    /// <summary>The tab of the open quote that is shown.</summary>
    public QuoteSection Section
    {
        get => _section;
        set
        {
            if (_section == value) return;
            ActiveTool.Cancel();
            Interaction.Clear();
            _section = value;
            OnPropertyChanged();
            Designs.IsVisible = _page == AppPage.Quote && value == QuoteSection.Designs;
            if (value == QuoteSection.Client) Details.SyncFromModel();
            if (value == QuoteSection.Pricing) Pricing.SyncFromModel();
            RefreshDocumentsIfShown();
            OnViewChanged();
        }
    }

    /// <summary>"QT-00012 · Sharma residence" for the quote header (just the name until the quote is saved and numbered).</summary>
    public string QuoteHeader
        => string.IsNullOrEmpty(Project.Quote.Number) ? Project.Name : $"{Project.Quote.NumberText} · {Project.Name}";

    /// <summary>"Mr. Archit Singh · Active", or just the status without a client.</summary>
    public string QuoteSubHeader
    {
        get
        {
            string client = Project.Quote.Client.DisplayName;
            return client.Length == 0 ? Project.Quote.Status.ToString() : $"{client} · {Project.Quote.Status}";
        }
    }

    /// <summary>"Qty 4 · 2,45,000.00 INR" for the quote header.</summary>
    public string QuoteTotalText
    {
        get
        {
            var totals = QuoteTotals.Of(Project);
            if (totals.Designs == 0) return "No designs";
            if (!Access.CanSeeQuoteValues) return $"Qty {totals.Quantity}";
            var value = QuoteValueOf();
            return $"Qty {totals.Quantity} · {value.Amount.ToString("N2", CultureInfo.InvariantCulture)} {value.Currency}".TrimEnd();
        }
    }

    private void RefreshQuoteViews()
    {
        InvalidatePrice();
        Designs.Invalidate();
        if (_section == QuoteSection.Pricing) Pricing.SyncFromModel();
        Details.SyncFromModel();
        RefreshDocumentsIfShown();
        OnPropertyChanged(nameof(QuoteHeader));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(QuoteSubHeader));
        OnPropertyChanged(nameof(QuoteTotalText));
    }

    /// <summary>The quote's value: the grand total of its price (designs, discount, charges and tax).</summary>
    public QuoteValue QuoteValueOf() => new(Price.GrandTotal, Calculation.Result.Currency);

    // ── Quotes ──────────────────────────────────────────────────────

    /// <summary>Starts a new, empty quote (asking first about unsaved changes) and shows its Client tab.</summary>
    public void NewQuote()
    {
        if (!ConfirmDiscardChanges()) return;
        ShowProject(new Project { Name = "New quote", Pricing = DefaultPricing() });
        Page = AppPage.Quote;
        Section = QuoteSection.Client;
    }

    /// <summary>Opens a saved quote (asking first about unsaved changes) on its Designs tab. Returns an error or null.</summary>
    public string? OpenQuote(Guid id)
    {
        if (id == Project.Id && Store?.Projects.Exists(id) == true && !IsDirty)
        {
            Page = AppPage.Quote;
            return null;
        }
        if (!ConfirmDiscardChanges()) return null;
        string? error = OpenProject(id);
        if (error is not null) return error;
        Page = AppPage.Quote;
        Section = QuoteSection.Designs;
        return null;
    }

    private string? SetQuoteDetails(string name, QuoteInfo quote)
        => RunForMessage(() => new SetQuoteInfoCommand(Project, name, quote));

    // ── Designs ─────────────────────────────────────────────────────

    /// <summary>Shows a design in the Drawing tab, selected and fitted.</summary>
    public void EditDesign(Guid frameId)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return;
        IsOutsideView = false;
        Page = AppPage.Quote;
        Section = QuoteSection.Drawing;
        Select(frame.Id);
        Canvas.FitToBounds(Rendering.FrameRenderer.DrawnBounds(frame).Bounds);
    }

    /// <summary>A copy of the design to the right of the others, with the next reference. One undo step.</summary>
    public string? DuplicateDesign(Guid frameId)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return "The design no longer exists.";
        return RunForMessage(() => DuplicateFrameCommand.Create(Project, frame, Rules));
    }

    /// <summary>Deletes a design after confirmation. One undo step.</summary>
    public string? DeleteDesign(Guid frameId)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return "The design no longer exists.";
        string name = string.IsNullOrWhiteSpace(frame.Design.Reference) ? "this design" : frame.Design.Reference;
        if (Dialogs is not null && !Dialogs.Confirm("Delete design", $"Delete {name} from the quote?"))
            return null;
        return RunForMessage(() => new DeleteFrameCommand(Project, frame));
    }

    /// <summary>A new plain frame (the New Frame size) next to the others, opened in the Drawing tab with the design library.</summary>
    public void NewDesign()
    {
        ClearSelection();
        CreateFrame();
        Page = AppPage.Quote;
        Section = QuoteSection.Drawing;
        DesignLibrary.SelectedCategory = Core.Design.DesignTemplates.Openable;
    }
}
