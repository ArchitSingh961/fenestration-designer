using System.IO;
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

    [Fact]
    public void CancellationWarrantyAndInstallation_AreSectionsAfterTheTerms_WhenFilled()
    {
        var vm = QuotationPdfTests.Quote();
        var settings = new QuotationSettings
        {
            CancellationPolicy = "Cancel within 3 days.\nNo cancellation after fabrication.",
            InstallationPrerequisites = "Openings plastered.\n\nPower on site."
        };

        var doc = QuotationPdfTests.OnSta(() => QuotationPdfTests.Document(vm, settings));

        Assert.Equal(new[] { "Cancellation Policy", "Pre-requisites for Installation" }, doc.Sections.Select(x => x.Title));   // empty warranty left out
        Assert.Equal(new[] { "Openings plastered.", "Power on site." }, doc.Sections[1].Points);
        Assert.Empty(QuotationPdfTests.OnSta(() => QuotationPdfTests.Document(vm, new QuotationSettings())).Sections);
    }

    [Fact]
    public void TheSections_AreTypedAndSaved_InQuotationSetup()
    {
        var vm = Start();
        vm.Page = AppPage.QuotationSetup;
        vm.QuotationSetup.DefaultSectionCommand.Execute("warranty");
        vm.QuotationSetup.CancellationPolicy = "Our cancellation policy.";

        vm.Page = AppPage.Dashboard;

        var saved = vm.Store!.Settings.LoadQuotationSettings();
        Assert.Equal(QuotationSettings.DefaultWarranty, saved.Warranty);
        Assert.Equal("Our cancellation policy.", saved.CancellationPolicy);
        Assert.Equal("", saved.InstallationPrerequisites);
        vm.Page = AppPage.QuotationSetup;
        Assert.Equal("Our cancellation policy.", vm.QuotationSetup.CancellationPolicy);
    }

    [Fact]
    public void TermsTheCompanyNumberedItself_AndAllSections_AreWritten()
    {
        var vm = QuotationPdfTests.Quote();
        var settings = new QuotationSettings
        {
            Letter = "Dear Customer,\nWe are delighted to send this proposal.\na. Window design, specification and value\nb. Terms and Conditions",
            Terms = "1. Payments terms:\na. 100% advance along with order.\nb. 50% advance with order, 50% before delivery.\n" +
                    "2. Validity of quote 30 days.\nBank Details :\nAccount Name",
            CancellationPolicy = QuotationSettings.DefaultCancellationPolicy,
            Warranty = QuotationSettings.DefaultWarranty,
            InstallationPrerequisites = QuotationSettings.DefaultInstallationPrerequisites
        };
        var doc = QuotationPdfTests.OnSta(() => QuotationPdfTests.Document(vm, settings));

        byte[] pdf = QuotationPdfTests.OnSta(() =>
        {
            using var stream = new MemoryStream();
            QuotationPdf.Write(doc, stream);
            return stream.ToArray();
        });

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(3, doc.Sections.Count);
        if (Environment.GetEnvironmentVariable("MARK_SECTIONS_OUT") is { Length: > 0 } path)
            File.WriteAllBytes(path, pdf);
    }
}
