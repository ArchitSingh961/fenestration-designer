using System.IO;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Production;
using Mark.Designer.ViewModels;
using Mark.Reports;
using Mark.Tests.Data;
using Mark.Tests.Sales;
using Xunit;

namespace Mark.Tests.Production;

/// <summary>Milestone 16: production orders from confirmed orders, their papers, progress per window and offcuts in stock.</summary>
public class ProductionTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A saved order like the quotation sample: W1 casement × 3 (uPVC with steel), W2 sliding in frosted glass, a fixed window.</summary>
    private MainViewModel Order()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Sample residence" });
        var library = vm.Library;
        vm.Project.Name = "Sample residence";
        vm.Project.Quote.Client = new ClientInfo { Title = "Mr.", FirstName = "Test", LastName = "Client", City = "Jaipur" };
        var casement = DesignTemplates.ForSystem(library.FindSystem("SYS-UPVC-62C")!, library).First(t => t.Openings.Any(o => o!.Value.IsHinged()));
        var sliding = DesignTemplates.ForSystem(library.FindSystem("SYS-AL-SL60")!, library).First(t => t.Openings.Any(o => o!.Value.IsSliding()));
        Assert.Null(vm.ApplyDesign(casement, "SYS-UPVC-62C"));
        vm.ClearSelection();
        Assert.True(((Mark.Designer.Interaction.IViewportDropTarget)vm).Drop(new Mark.Core.Geometry.Point2D(2500, 0), $"{sliding.Id}@SYS-AL-SL60"));
        var slidingFrame = vm.Project.Frames[1];
        vm.CommandHistory.Execute(new AssignGlassCommand(slidingFrame, slidingFrame.GlassPanels.Select(g => g.Id).ToList(), "GLS-FRS-5", vm.Library, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Quantity = 3;
        slidingFrame.Design.Reference = "W2";
        vm.ClearSelection();
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 6000, 0, 900, 1200, vm.Rules));
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.ConvertToOrder());
        return vm;
    }

    [Fact]
    public void AnOrder_GoesIntoProduction_WithItsDesignsAsTheyWere()
    {
        var vm = Order();

        Assert.Null(vm.StartProduction());

        Assert.Equal(AppPage.ProductionOrders, vm.Page);
        var row = vm.Production.Orders.Single();
        Assert.Equal(vm.Project.Quote.OrderNumber, row.OrderNumber);
        Assert.Equal(3, vm.Production.Windows.Count);
        Assert.Equal(new[] { "W1", "W2", "W3" }, vm.Production.Windows.Select(w => w.Reference));
        Assert.Equal(3, vm.Production.Windows[0].Quantity);
        Assert.Equal("Not started", vm.Production.StageText);
        Assert.Empty(vm.Production.AvailableOrders);                                     // this order is in production now

        // Changes to the quote afterwards do not change what is being made.
        vm.Page = AppPage.Quote;
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 9000, 0, 900, 1200, vm.Rules));
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.StartProduction());                                               // opens the same one
        Assert.Single(vm.Production.Orders);
        Assert.Equal(3, vm.Production.Windows.Count);
    }

    [Fact]
    public void OnlyASavedOrder_CanGoIntoProduction()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs { PromptAnswer = "Quote" });
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));

        Assert.Contains("Convert the quote to an order", vm.StartProduction());
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.ConvertToOrder());
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 1200, 1500, vm.Rules));
        Assert.Contains("Save the order first", vm.StartProduction());
    }

    [Fact]
    public void Progress_IsKeptPerWindowAndStep()
    {
        var vm = Order();
        vm.StartProduction();
        var production = vm.Production;
        var w1 = production.Windows[0];

        w1.Steps.Single(s => s.Step == ProductionStep.Cut).MoreCommand.Execute(null);
        Assert.Equal("1 / 3", w1.Steps[0].Text);
        Assert.True(w1.Steps[0].IsStarted);
        Assert.Equal("In production", production.StageText);
        w1.Steps[0].ToggleCommand.Execute(null);                                          // all three
        Assert.True(w1.Steps[0].IsComplete);

        production.MarkAllCommand.Execute(ProductionStep.Cut);
        production.MarkAllCommand.Execute(ProductionStep.Assembled);
        Assert.Equal("All assembled", production.StageText);
        Assert.Equal(0.4, production.Fraction, 6);

        // Kept: read again from the database.
        production.Reload();
        Assert.Equal("All assembled", production.Orders.Single().StageText);
        Assert.True(production.Windows.All(w => w.Steps[1].IsComplete && !w.Steps[2].IsStarted));
        foreach (var step in ProductionOrder.Steps) production.MarkAllCommand.Execute(step);
        Assert.Equal("Dispatched", production.StageText);
    }

    [Fact]
    public void ThePapers_CoverEveryPiecePaneAndPart()
    {
        var vm = Order();
        vm.StartProduction();

        var papers = QuotationPdfTests.OnSta(() => vm.Production.BuildPapers()!);
        var doc = papers.Document;

        int pieces = papers.Result.Profiles.Where(p => p.IsResolved).Sum(p => p.Quantity);
        Assert.Equal(pieces, doc.Profiles.Sum(p => p.Bars.Sum(b => b.Pieces.Count)));
        Assert.Contains(doc.Profiles, p => p.IsSteel);                                     // the uPVC frame's steel
        Assert.Contains(doc.Profiles.SelectMany(p => p.Bars).SelectMany(b => b.Pieces), p => p.Where.StartsWith("W1 · frame top"));
        int panes = papers.Result.Glass.Where(g => g.IsResolved).Sum(g => g.Quantity);
        Assert.Equal(panes, doc.Glass.Sum(g => g.Quantity));
        Assert.Contains(doc.Glass, g => g.Name.Contains("Frosted", StringComparison.OrdinalIgnoreCase) && g.Where.StartsWith("W2"));
        Assert.Equal(pieces + panes, doc.Labels.Count);
        Assert.Equal($"P{pieces}", doc.Labels.Last(l => l.Number.StartsWith('P')).Number);
        Assert.NotEmpty(doc.Hardware);
        Assert.Equal(3, doc.Drawings.Count);
        Assert.All(doc.Drawings, d => Assert.NotNull(d.Drawing));
        Assert.Contains(doc.Drawings[0].Lines, l => l.Group == "Glass");

        foreach (var sheet in Enum.GetValues<ProductionSheet>())
        {
            byte[] pdf = QuotationPdfTests.OnSta(() =>
            {
                using var stream = new MemoryStream();
                ProductionPdf.Write(doc, sheet, stream);
                return stream.ToArray();
            });
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            if (Environment.GetEnvironmentVariable("MARK_PRODUCTION_OUT") is { Length: > 0 } folder)
                File.WriteAllBytes(Path.Combine(folder, $"{sheet}.pdf"), pdf);
        }
        Assert.InRange(QuotationPdfTests.OnSta(() => ProductionPdf.CountPages(doc, ProductionSheet.ShopDrawings)), 3, 6);   // a page or two per window
        int labelPages = QuotationPdfTests.OnSta(() => ProductionPdf.CountPages(doc, ProductionSheet.Labels));
        Assert.Equal((pieces + panes + 20) / 21, labelPages);                              // 21 labels to a sheet
    }

    [Fact]
    public void Offcuts_AreCutFirst_AndUpdatedOnceAfterCutting()
    {
        var vm = Order();
        vm.StartProduction();
        var store = vm.Store!;
        var production = vm.Production;
        var firstPiece = QuotationPdfTests.OnSta(() => production.BuildPapers()!).Plan.Profiles.First(p => p.Bars.Count > 0);
        double longest = firstPiece.Bars.SelectMany(b => b.Cuts).Max(c => c.CutLengthMm);
        store.Production.AddOffcuts(firstPiece.DefinitionId, longest + 50, 1, "");

        var plan = QuotationPdfTests.OnSta(() => production.BuildPapers()!).Plan;
        long offcutId = store.Production.Offcuts().Single().Id;
        Assert.Contains(offcutId, plan.OffcutIdsUsed);
        production.UseOffcuts = false;
        Assert.Empty(QuotationPdfTests.OnSta(() => production.BuildPapers()!).Plan.OffcutIdsUsed);
        production.UseOffcuts = true;

        int newOffcuts = plan.Remnants.Count();
        Assert.Null(QuotationPdfTests.OnSta(() => production.UpdateOffcuts()));
        var stock = store.Production.Offcuts();
        Assert.DoesNotContain(stock, o => o.LengthMm == longest + 50);                    // the offcut used is out of stock
        Assert.Equal(newOffcuts, stock.Count);
        Assert.All(stock, o => Assert.Equal(vm.Project.Quote.OrderNumber, o.Source));
        Assert.Contains("already updated", QuotationPdfTests.OnSta(() => production.UpdateOffcuts()));   // once only
    }

    [Fact]
    public void Offcuts_CanBeAddedAndRemovedByHand()
    {
        var vm = Order();
        var offcuts = vm.Offcuts;
        vm.ShowView(AppView.Offcuts, AppArea.Production);
        offcuts.Profile = offcuts.Profiles.First(p => p.Id == "PRF-FRM-60");
        offcuts.LengthText = "1450";
        offcuts.CountText = "2";
        offcuts.AddCommand.Execute(null);

        Assert.False(offcuts.MessageIsError, offcuts.Message);
        var group = offcuts.Groups.Single();
        Assert.Equal(2, group.Offcuts.Count);
        Assert.Contains("2 offcuts", group.Summary);
        offcuts.RemoveCommand.Execute(group.Offcuts[0]);
        Assert.Single(offcuts.Groups.Single().Offcuts);

        offcuts.LengthText = "abc";
        offcuts.AddCommand.Execute(null);
        Assert.True(offcuts.MessageIsError);
    }

    [Fact]
    public void ProductionOrders_AreInTheProductionArea_AndNeedTheirFeature()
    {
        var info = AreaCatalog.Of(AppArea.Production);
        Assert.Equal(new[] { AppView.ProductionOrders, AppView.Cutting, AppView.Offcuts }, info.Tabs.Select(t => t.View));
        Assert.True(Mark.Licensing.FeatureCatalog.All.Single(f => f.Id == Mark.Licensing.Features.ProductionOrders).IsBuilt);
    }
}
