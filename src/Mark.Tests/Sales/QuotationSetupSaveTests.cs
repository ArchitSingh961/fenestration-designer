using Mark.Core.Commands;
using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.Reports;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>What is typed in Quotation setup reaches the quotation, even without Save setup; long terms take more pages.</summary>
public class QuotationSetupSaveTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string LongTerms(int count)
        => string.Join("\n", Enumerable.Range(1, count).Select(i =>
            $"Term {i}: the prices, sizes, delivery, installation, payment and warranty conditions of this quotation apply as written here, " +
            "and anything not written here is agreed separately in writing before the order is placed."));

    private MainViewModel Start()
    {
        var vm = new MainViewModel(_temp.Open(TempDatabase.ShippedLibraryPath), null, new FakeDialogs());
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        return vm;
    }

    [Fact]
    public void TypedTerms_AreSaved_WhenLeavingThePage()
    {
        var vm = Start();
        vm.Page = AppPage.QuotationSetup;
        vm.QuotationSetup.Terms = "Our own first term.\nOur own second term.";

        vm.Page = AppPage.Dashboard;                                                 // no Save setup

        Assert.Equal("Our own first term.\nOur own second term.", vm.Store!.Settings.LoadQuotationSettings().Terms);
    }

    [Fact]
    public void TypedTerms_AreUsed_ByTheQuotation_WithoutSaveSetup()
    {
        var vm = Start();
        vm.Page = AppPage.QuotationSetup;
        vm.QuotationSetup.Terms = "Only our term.";

        var doc = QuotationPdfTests.OnSta(vm.BuildQuotation);

        Assert.Equal(new[] { "Only our term." }, doc.Terms);
    }

    [Fact]
    public void A_SetupNeverOpened_IsNotSavedOver()
    {
        var vm = Start();
        vm.Store!.Settings.SaveQuotationSettings(new QuotationSettings { Terms = "Saved before." });

        vm.Page = AppPage.Dashboard;
        QuotationPdfTests.OnSta(vm.BuildQuotation);

        Assert.Equal("Saved before.", vm.Store.Settings.LoadQuotationSettings().Terms);
        Assert.False(vm.QuotationSetup.HasChanges);
    }

    [Fact]
    public void LongTerms_ContinueOnTheNextPages()
    {
        var vm = QuotationPdfTests.Quote();
        int Pages(int terms) => QuotationPdfTests.OnSta(() => QuotationPdf.CountPages(
            QuotationPdfTests.Document(vm, new QuotationSettings { Terms = LongTerms(terms) })));

        int few = Pages(6);
        int many = Pages(80);

        Assert.True(many >= few + 2, $"6 terms: {few} pages, 80 terms: {many} pages");
        var doc = QuotationPdfTests.OnSta(() => QuotationPdfTests.Document(vm, new QuotationSettings { Terms = LongTerms(80) }));
        Assert.Equal(80, doc.Terms.Count);                                            // none dropped
    }
}
