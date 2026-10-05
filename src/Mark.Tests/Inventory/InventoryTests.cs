using System.IO;
using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Inventory;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Reports;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Inventory;

/// <summary>
/// Milestone 18, purchasing and inventory: what a job needs from stock, stock reserved for production orders, purchase
/// orders from what is short, goods received into stock, issuing to production, counts, reorder levels and suppliers.
/// </summary>
public class InventoryTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A confirmed order in production: W1 1200 × 1500 × 3 and W2 900 × 1200.</summary>
    private (MainViewModel Vm, LocalStore Store, FakeDialogs Dialogs) InProduction()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var dialogs = new FakeDialogs { PromptAnswer = "Kapoor house" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.Project.Name = "Kapoor house";
        vm.Project.Quote.Client = new ClientInfo { FirstName = "Amit", LastName = "Kapoor", City = "Jaipur" };
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Quantity = 3;
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 900, 1200, vm.Rules));
        vm.Project.Frames[1].Design.Reference = "W2";
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.ConvertToOrder());
        Assert.Null(vm.StartProduction());
        return (vm, store, dialogs);
    }

    [Fact]
    public void AJob_NeedsBarsGlassAndHardware()
    {
        var (vm, _, _) = InProduction();
        var needs = StockNeeds.For(vm.Project, vm.Library, new CalculationRules());
        var result = vm.Calculation.Result;

        Assert.Contains(needs, n => n.Key.Kind == StockKind.Profile && n.Unit == "bars" && n.Quantity >= 1);
        var glass = needs.Where(n => n.Key.Kind == StockKind.Glass).ToList();
        Assert.Equal(Math.Round(result.Bom.Where(b => b.Category == BomCategory.Glass).Sum(b => b.AreaM2 ?? 0), 3), glass.Sum(g => g.Quantity), 3);
        // Bars: as many as the cutting plan takes new.
        var plan = new CuttingOptimizer().Optimize(result.Profiles, vm.Library, new CalculationRules());
        Assert.Equal(plan.Profiles.Sum(p => p.Stock.Sum(s => s.Quantity)), (int)needs.Where(n => n.Key.Kind == StockKind.Profile).Sum(n => n.Quantity));
    }

    [Fact]
    public void TheLedger_AddsUpToWhatIsOnHand()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var key = new StockKey(StockKind.Glass, "GLS-CLR-6");
        store.Inventory.Move(new[] { (key, 20.0) }, StockMoveReason.Adjusted, "Opening stock", "Ravi");
        store.Inventory.Move(new[] { (key, -4.5) }, StockMoveReason.Issued, "OR-00001", "Ravi");
        store.Inventory.SetLevel(key, 10, "Rack 3");

        var level = store.Inventory.Levels().Single();
        Assert.Equal(15.5, level.OnHand, 6);
        Assert.Equal(10, level.ReorderLevel);
        Assert.Equal("Rack 3", level.Location);
        Assert.Equal(new[] { -4.5, 20.0 }, store.Inventory.Moves(key).Select(m => m.Quantity));
        Assert.Contains("OR-00001", store.Inventory.IssuedOrders());
    }

    [Fact]
    public void FromShortToOrderedToReceivedToIssued()
    {
        var (vm, store, dialogs) = InProduction();
        var inv = vm.Inventory;
        vm.ShowView(AppView.Suppliers);
        Assert.Equal(AppPage.Suppliers, vm.Page);

        // A supplier.
        inv.NewSupplierCommand.Execute(null);
        inv.Editing!.Name = "Jaipur Aluminium Traders";
        inv.Editing.Phone = "0141 000000";
        Assert.Null(inv.SaveSupplier());
        Assert.Equal("Jaipur Aluminium Traders", inv.Suppliers.Single().Name);
        inv.NewSupplierCommand.Execute(null);
        inv.Editing!.Name = "jaipur aluminium traders";
        Assert.Contains("already a supplier", inv.SaveSupplier());

        // Everything the order needs is reserved and short.
        vm.ShowView(AppView.Stock);
        Assert.NotEmpty(inv.Stock);
        Assert.All(inv.Stock, r => Assert.True(r.Position.Reserved > 0));
        Assert.All(inv.Stock, r => Assert.True(r.IsLow));
        Assert.True(inv.HasLow);
        Assert.Single(inv.Issues);

        // A purchase order of what is short, placed: now it is on order and nothing more is short.
        vm.ShowView(AppView.PurchaseOrders);
        inv.PurchaseSupplier = inv.ActiveSuppliers.Single();
        Assert.Null(inv.Suggest());
        var po = inv.Purchase!;
        Assert.Equal("PO-00001", po.Number);
        Assert.Equal(PurchaseStatus.Draft, po.Status);
        Assert.Equal(inv.Stock.Count, po.Lines.Count);
        Assert.Contains(vm.Project.Quote.OrderNumber, po.ForOrders);
        Assert.All(po.Lines, l => Assert.True(l.Rate >= 0));
        Assert.Null(inv.PlaceOrder());
        Assert.Equal(PurchaseStatus.Ordered, inv.Purchase!.Status);
        Assert.StartsWith("Nothing to buy", inv.ShortfallText);
        Assert.All(inv.Stock, r => Assert.True(r.Position.OnOrder > 0));

        // The PDF for the supplier.
        string pdf = Path.Combine(_temp.Folder, "po.pdf");
        Assert.Null(inv.PurchasePdfFile(pdf));
        Assert.True(new FileInfo(pdf).Length > 1000);

        // Part of the first line comes in, then the rest.
        var first = inv.Lines[0];
        double ordered = first.Line.Quantity;
        foreach (var row in inv.Lines) row.ReceiveText = "0";
        inv.Lines[0].ReceiveText = "1";
        inv.SupplierInvoice = "INV-778";
        Assert.Null(inv.Receive());
        Assert.Equal(PurchaseStatus.PartReceived, inv.Purchase!.Status);
        Assert.Equal("GRN-00001", inv.Purchase.Receipts.Single().Number);
        Assert.Equal(1, store.Inventory.Levels().Single(l => l.Key == first.Line.Key).OnHand);
        Assert.Null(inv.Receive());                                      // the rest (each line defaults to what is still due)
        Assert.Equal(PurchaseStatus.Received, inv.Purchase!.Status);
        Assert.Equal(ordered, store.Inventory.Levels().Single(l => l.Key == first.Line.Key).OnHand, 6);

        // Stock covers the order now; issuing it takes it out and frees the reservation.
        vm.ShowView(AppView.Stock);
        Assert.False(inv.HasLow);
        inv.Issues.Single().IssueCommand.Execute(null);
        Assert.Contains("Take what", dialogs.Confirms.Last());
        Assert.Empty(inv.Issues);
        Assert.All(inv.Stock, r => Assert.Equal(0, r.Position.Reserved));
        Assert.Contains(store.Inventory.Moves(), m => m.Reason == StockMoveReason.Issued);
    }

    [Fact]
    public void CountsAdjustmentsAndReorderLevels()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs());
        var inv = vm.Inventory;
        vm.ShowView(AppView.Stock);
        Assert.True(inv.IsStockEmpty);

        inv.NewStockItem = inv.Choices.First(c => c.Key.Kind == StockKind.Material);
        Assert.Null(inv.AddStockItem());
        Assert.Equal(inv.NewStockItem!.Key, inv.SelectedStock!.Position.Key);

        inv.CountText = "50";
        Assert.Null(inv.Count());
        Assert.Equal("50", inv.SelectedStock!.OnHand);
        inv.AdjustText = "-8";
        inv.NoteText = "damaged";
        Assert.Null(inv.Adjust());
        Assert.Equal("42", inv.SelectedStock!.OnHand);
        Assert.Equal("damaged", store.Inventory.Moves().First().Note);

        inv.ReorderText = "45";
        inv.LocationText = "Shelf B";
        Assert.Null(inv.SaveStockSettings());
        Assert.True(inv.SelectedStock!.IsLow);
        Assert.Equal("1 item is low on stock: see what to buy in Purchasing.", inv.LowText);
        Assert.Equal("3", inv.SelectedStock.ToBuy);                      // back up to the reorder level

        inv.CountText = "abc";
        Assert.NotNull(inv.Count());
    }

    [Fact]
    public void ADraft_CanBeEditedButAPlacedOrderCannot()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, new FakeDialogs());
        var inv = vm.Inventory;
        store.Inventory.SaveSupplier(new Supplier { Name = "Glass House" });
        vm.ShowView(AppView.PurchaseOrders);
        inv.PurchaseSupplier = inv.ActiveSuppliers.Single();

        Assert.Contains("Nothing Glass House supplies is short", inv.Suggest());
        Assert.Null(inv.NewPurchase());
        Assert.Equal("Choose the item to add.", inv.AddLine());
        inv.LineItem = inv.Choices.First(c => c.Key.Kind == StockKind.Glass);
        inv.LineQuantityText = "12.5";
        Assert.Null(inv.AddLine());
        inv.Lines.Single().RateText = "900";
        inv.ExpectedText = "20-10-2026";
        Assert.Null(inv.SavePurchase());
        Assert.Equal(11250m, inv.Purchase!.Total);
        Assert.Equal(new DateTime(2026, 10, 20), inv.Purchase.ExpectedDate);

        Assert.Null(inv.PlaceOrder());
        Assert.True(inv.IsLocked);
        Assert.Contains("draft", inv.AddLine());
        Assert.Null(inv.CancelPurchase());
        Assert.Equal(PurchaseStatus.Cancelled, inv.Purchase!.Status);
        Assert.Contains("ordered", inv.Receive());
    }

    [Fact]
    public void ThePurchaseOrderPdf_IsWritten()
    {
        var paper = new PurchasePaper
        {
            CompanyName = "Test Windows", SupplierName = "Glass House", Number = "PO-00007", Date = "06-10-2026", Currency = "INR",
            Lines = new[] { new PurchasePaperLine("6mm Toughened Clear", "12.5", "m²", "900.00", "11,250.00") }, Total = "11,250.00"
        };
        using var stream = new MemoryStream();
        PurchasePdf.Write(paper, stream);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(stream.ToArray(), 0, 4));
    }

    [Fact]
    public void Numbers_FollowTheHighest()
    {
        Assert.Equal("PO-00001", PurchaseOrder.NextNumber("PO", Array.Empty<string>()));
        Assert.Equal("PO-00013", PurchaseOrder.NextNumber("PO", new[] { "PO-00002", "PO-00012", "junk" }));
    }
}
