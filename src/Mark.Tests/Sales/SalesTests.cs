using System.IO;
using Mark.Core.Commands;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>Milestone 15: enquiries, enquiry → quote → order, quote revisions, the quotation PDF and the sales charts.</summary>
public class SalesTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private (MainViewModel Vm, LocalStore Store) Start()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Quote" });
        return (vm, store);
    }

    private static Enquiry Enquiry(string first = "Archit", string city = "Jaipur") => new()
    {
        Client = new ClientInfo { Title = "Mr.", FirstName = first, LastName = "Singh", Phone = "98290 00000", City = city },
        Source = "Referral", Owner = "Ravi", ExpectedValue = 120000, Requirements = "Bedroom and living room, toughened glass"
    };

    private static void AddDesign(MainViewModel vm)
        => vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, vm.Project.Frames.Count * 2000, 0, 1200, 1500, vm.Rules));

    // ── Enquiries ───────────────────────────────────────────────────

    [Fact]
    public void Enquiries_AreNumbered_Listed_AndDeleted()
    {
        var store = _temp.Open();
        store.Enquiries.User = new ProjectUser("ravi", "Ravi Shah");
        var first = Enquiry();
        var second = Enquiry("Neha", "Udaipur");

        store.Enquiries.Save(first);
        _temp.Tick();
        store.Enquiries.Save(second);

        Assert.Equal("EN-00001", first.Number);
        Assert.Equal("EN-00002", second.Number);
        var list = store.Enquiries.List();
        Assert.Equal(new[] { "EN-00002", "EN-00001" }, list.Select(e => e.Number));
        Assert.Equal("Ravi Shah", list[1].CreatedBy);
        Assert.Equal("Jaipur", list[1].City);
        Assert.Equal(120000m, list[1].ExpectedValue);
        Assert.Equal("Bedroom and living room, toughened glass", store.Enquiries.Load(first.Id).Requirements);

        store.Enquiries.Delete(first.Id);
        Assert.Single(store.Enquiries.List());
    }

    [Fact]
    public void TheForm_HasTwoSteps_AndChecksEach()
    {
        var (vm, _) = Start();
        var page = vm.Enquiries;
        page.Reload();

        page.NewCommand.Execute(null);
        page.NextCommand.Execute(null);
        Assert.Equal(1, page.Editor!.Step);
        Assert.Equal("Enter the client's name or company.", page.Message);

        page.Editor.FirstName = "Archit";
        page.Editor.Phone = "98290 00000";
        page.NextCommand.Execute(null);
        Assert.Equal(2, page.Editor.Step);
        page.Editor.ExpectedValue = "lots";
        Assert.Null(page.Save());
        Assert.Equal("Enter the expected value as a number (or leave it empty).", page.Message);

        page.Editor.ExpectedValue = "1,50,000";
        page.Editor.FollowUp = DateTime.Today;
        var saved = page.Save()!;
        Assert.Equal(150000m, saved.ExpectedValue);
        Assert.Single(page.Rows);
        Assert.Equal("1 follow-up due", page.DueText);
        Assert.Equal("Open (1)", page.OpenHeader);
    }

    [Fact]
    public void AnEnquiry_BecomesAQuote_AndTheQuotesResultBecomesTheEnquirys()
    {
        var (vm, store) = Start();
        var enquiry = Enquiry();
        store.Enquiries.Save(enquiry);

        Assert.Null(vm.CreateQuoteFromEnquiry(enquiry));

        Assert.Equal("Mr. Archit Singh", vm.Project.Quote.Client.DisplayName);
        Assert.Equal("Bedroom and living room, toughened glass", vm.Project.Quote.Notes);
        Assert.Equal(enquiry.Id, vm.Project.Quote.EnquiryId);
        Assert.Equal(AppView.Client, vm.CurrentView);
        Assert.Equal(EnquiryStage.Quoted, store.Enquiries.Load(enquiry.Id).Stage);

        AddDesign(vm);
        Assert.Null(vm.SaveProject());
        Assert.Equal("QT-00001", store.Enquiries.List().Single().QuoteNumber);

        var won = vm.Project.Quote.Copy();
        won.Status = QuoteStatus.Won;
        vm.CommandHistory.Execute(new SetQuoteInfoCommand(vm.Project, vm.Project.Name, won));
        Assert.Null(vm.SaveProject());
        Assert.Equal(EnquiryStage.Won, store.Enquiries.Load(enquiry.Id).Stage);
    }

    // ── Orders and revisions ────────────────────────────────────────

    [Fact]
    public void ASavedQuote_BecomesAnOrder_WithANumber()
    {
        var (vm, store) = Start();
        AddDesign(vm);
        Assert.Equal("Save the quote first.", vm.ConvertToOrder());
        Assert.Null(vm.SaveProject());

        Assert.Null(vm.ConvertToOrder());

        Assert.Equal("OR-00001", vm.Project.Quote.OrderNumber);
        Assert.Equal(QuoteStatus.Won, vm.Project.Quote.Status);
        Assert.StartsWith("Order OR-00001", vm.OrderText);
        var summary = store.Projects.List().Single();
        Assert.Equal("OR-00001", summary.OrderNumber);
        Assert.NotNull(summary.DecidedUtc);
        Assert.Contains("Order OR-00001", store.Projects.History(vm.Project.Id)[0].Detail);
        Assert.Equal($"This quote is already order OR-00001.", vm.ConvertToOrder());
    }

    [Fact]
    public void ANewRevision_KeepsTheSavedQuote_AndCountsOn()
    {
        var (vm, store) = Start();
        AddDesign(vm);
        Assert.Null(vm.SaveProject());
        decimal first = store.Projects.List().Single().Value!.Value;

        Assert.Null(vm.NewRevision());
        AddDesign(vm);
        Assert.Null(vm.SaveProject());

        Assert.Equal(1, vm.Project.Quote.Revision);
        Assert.StartsWith("QT-00001 R1 · ", vm.QuoteHeader);
        Assert.Equal("QT-00001 R1", store.Projects.List().Single().NumberText);
        var kept = Assert.Single(vm.QuoteRevisions);
        Assert.Equal("R0", kept.Name);
        Assert.Equal(first, store.Projects.Revisions(vm.Project.Id).Single().Value);
        Assert.Single(store.Projects.LoadRevision(vm.Project.Id, 0).Frames);

        kept.OpenCommand.Execute(null);
        Assert.Single(vm.Project.Frames);
        Assert.Equal("", vm.Project.Quote.Number);
        Assert.EndsWith("(R0 copy)", vm.Project.Name);
    }

    [Fact]
    public void ChangingTheClient_KeepsTheRevisionOrderAndEnquiry()
    {
        var project = new Project();
        var enquiry = Guid.NewGuid();
        project.Quote.Revision = 2;
        project.Quote.OrderNumber = "OR-00004";
        project.Quote.EnquiryId = enquiry;

        QuoteEditor.SetQuote(project, "Renamed", new QuoteInfo { Client = new ClientInfo { FirstName = "Neha" } });

        Assert.Equal(2, project.Quote.Revision);
        Assert.Equal("OR-00004", project.Quote.OrderNumber);
        Assert.Equal(enquiry, project.Quote.EnquiryId);
    }

    // ── Quotation PDF ───────────────────────────────────────────────

    [Fact]
    public void TheQuotation_IsWrittenForASavedQuote_WithTheSetup()
    {
        var (vm, store) = Start();
        string path = Path.Combine(_temp.Folder, "quotation.pdf");
        string? opened = null;
        vm.OpenDocument = p => opened = p;
        Assert.Equal("Add at least one design before making the quotation.", vm.ExportQuotation(path));
        AddDesign(vm);
        Assert.StartsWith("Save the quote first", vm.ExportQuotation(path));
        Assert.Null(vm.SaveProject());
        store.Settings.SaveQuotationSettings(new QuotationSettings { CompanyName = "Test Windows", Gstin = "08ABCDE1234F1Z5" });

        string? error = QuotationPdfTests.OnSta(() => vm.ExportQuotation(path));

        Assert.Null(error);
        Assert.Equal(path, opened);
        Assert.True(new FileInfo(path).Length > 10_000);
        var doc = QuotationPdfTests.OnSta(vm.BuildQuotation);
        Assert.Equal("Test Windows", doc.Company.Name);
        Assert.Equal("QT-00001", doc.QuoteNumber);
        Assert.Contains("GSTIN : 08ABCDE1234F1Z5", doc.Company.Lines);
    }

    [Fact]
    public void TheSetup_IsSavedAndLoaded()
    {
        var (vm, store) = Start();
        var setup = vm.QuotationSetup;
        setup.Load();
        Assert.Equal(QuotationSettings.DefaultTerms, setup.Terms);

        setup.Address = "Plot 1, Industrial Area";
        setup.Gstin = "08abcde1234f1z5";
        setup.IsSquareMetres = true;
        setup.Save();

        var saved = store.Settings.LoadQuotationSettings();
        Assert.Equal("08ABCDE1234F1Z5", saved.Gstin);
        Assert.Equal(AreaUnit.SquareMetres, saved.AreaUnit);
        Assert.False(setup.MessageIsError);
    }

    // ── Charts ──────────────────────────────────────────────────────

    [Fact]
    public void TheCharts_CountByPeriod_PersonAndCity()
    {
        var (vm, store) = Start();
        store.Enquiries.Save(Enquiry());
        AddDesign(vm);
        vm.Project.Quote.Client.City = "Jaipur";
        Assert.Null(vm.SaveProject());
        Assert.Null(vm.ConvertToOrder());

        var charts = new SalesChartsViewModel(() => store.Projects, () => store.Enquiries, () => TempDatabase.Start.ToLocalTime());
        charts.Reload();

        Assert.Equal(12, charts.Columns.Count);
        var current = charts.Columns.Last();
        Assert.Equal(new[] { 1, 1, 1, 0 }, current.Counts);
        Assert.True(current.WonValue > 0);
        Assert.Equal(SalesChartsViewModel.ChartHeight, current.Bars.Max(b => b.Height));
        Assert.Equal("Jaipur", charts.Cities.Single().City);
        Assert.Single(charts.People);
        Assert.StartsWith("Last 12 months: 1 enquiries, 1 quotes, 1 won", charts.SummaryText);

        charts.IsWeeks = true;
        Assert.Equal(new[] { 1, 1, 1, 0 }, charts.Columns.Last().Counts);
    }

    [Fact]
    public void Periods_AreTheLast12WeeksOrMonths()
    {
        var weeks = SalesChartsViewModel.Periods(new DateTime(2026, 10, 4), ChartPeriod.Weeks);   // a Sunday
        var months = SalesChartsViewModel.Periods(new DateTime(2026, 10, 4), ChartPeriod.Months);

        Assert.Equal(new DateTime(2026, 9, 28), weeks[^1].Start);                                  // its Monday
        Assert.Equal(DayOfWeek.Monday, weeks[0].Start.DayOfWeek);
        Assert.Equal(new DateTime(2025, 11, 1), months[0].Start);
        Assert.Equal("Oct", months[^1].Label);
    }
}
