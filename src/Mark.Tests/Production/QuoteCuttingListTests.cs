using System.IO;
using Mark.Core.Design;
using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Mark.Tests.Sales;
using Xunit;

namespace Mark.Tests.Production;

/// <summary>
/// The cutting list and the piece labels of a quote, in one PDF: no production order needed; every bar drawn and
/// listed, then the labels.
/// </summary>
public class QuoteCuttingListTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AQuote_MakesItsCuttingListAndLabels_InOnePdf()
    {
        string path = Path.Combine(_temp.Folder, "cut.pdf");
        Directory.CreateDirectory(_temp.Folder);
        var (error, pages, kept) = QuotationPdfTests.OnSta(() =>
        {
            var store = _temp.Open(TempDatabase.ShippedLibraryPath);
            var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Villa" });
            Assert.Contains("Add at least one design", vm.ExportCuttingAndLabels(path));
            vm.CreateFrame();
            Assert.Null(vm.ApplyDesign(DesignTemplates.Find("sld-2")!));
            vm.Project.Frames[0].Design.Quantity = 2;
            Assert.Null(vm.SaveProject());
            string? e = vm.ExportCuttingAndLabels(path);
            int keptCount = store.Documents.ForProject(vm.Project.Id).Count(d => d.Category == DocumentCategory.Others);
            return (e, Mark.Reports.ProductionPdf.CountPages(
                Mark.Designer.ViewModels.ProductionBuilder.Build(new ProductionInputs(new Mark.Core.Production.ProductionOrder
                {
                    OrderNumber = "QT", DocumentJson = Mark.Core.Serialization.ProjectSerializer.Serialize(vm.Project)
                }, vm.Library, vm.Calculation.Rules, vm.Rules, Array.Empty<Mark.Core.Production.Offcut>(), "Test", DateTime.UtcNow)).Document,
                Mark.Reports.ProductionSheet.CuttingListAndLabels), keptCount);
        });

        Assert.Null(error);
        Assert.StartsWith("%PDF", File.ReadAllText(path)[..4]);
        Assert.True(pages >= 2);                                                              // the list, then the labels
        Assert.Equal(1, kept);                                                                // a copy in Documents › Others
    }
}
