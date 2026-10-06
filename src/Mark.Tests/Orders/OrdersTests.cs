using System.IO;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Core.Orders;
using Mark.Core.Production;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Orders;

/// <summary>
/// Milestone 17: orders from confirmation to installation — stage, payments, delivery and installation schedule,
/// dispatch notes and the installation sign-off.
/// </summary>
public class OrdersTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mark-tests", "orders-" + Guid.NewGuid().ToString("N"));

    public OrdersTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _temp.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A saved and confirmed order: W1 casement × 3 and W2, a fixed window, for a client in Jaipur.</summary>
    private MainViewModel Order(FakeDialogs? dialogs = null)
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var vm = new MainViewModel(store, null, dialogs ?? new FakeDialogs { PromptAnswer = "Sharma residence" });
        vm.Project.Name = "Sharma residence";
        vm.Project.Quote.Client = new ClientInfo
        {
            Title = "Mr.", FirstName = "Rohit", LastName = "Sharma", Phone = "98290 00000", AddressLine1 = "12 Park Road", City = "Jaipur"
        };
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Quantity = 3;
        vm.Project.Frames[0].Design.Location = "Living room";
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 900, 1200, vm.Rules));
        vm.Project.Frames[1].Design.Reference = "W2";
        vm.SaveProjectCommand.Execute(null);
        Assert.Null(vm.ConvertToOrder());
        return vm;
    }

    [Fact]
    public void AConfirmedOrder_IsInTheOrderBook_WithItsQuotesClientAndValue()
    {
        var vm = Order();

        Assert.Null(vm.ShowOrder());

        Assert.Equal(AppPage.Orders, vm.Page);
        var book = vm.OrderBook;
        var row = book.Orders.Single();
        Assert.Equal(vm.Project.Quote.OrderNumber, row.OrderNumber);
        Assert.Equal("Confirmed", row.StageText);
        Assert.Contains("Sharma residence", book.OrderTitle);
        Assert.Contains("98290 00000", book.OrderDetail);
        Assert.Equal("12 Park Road, Jaipur", book.SiteText);
        Assert.NotEqual("Not priced", book.ValueText);
        Assert.Equal(book.ValueText, book.BalanceText);                                     // nothing received yet
        Assert.Equal(new[] { "W1", "W2" }, book.DispatchLines.Select(l => l.Reference));
        Assert.Equal("3", book.DispatchLines[0].SendText);                                  // everything left goes by default
        Assert.True(book.StageSteps.Single(s => s.Stage == OrderStage.Confirmed).IsCurrent);
    }

    [Fact]
    public void Payments_AreRecorded_AndTheBalanceFollows()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        var order = vm.Store!.Orders.ForProject(vm.Project.Id)!;
        decimal value = order.Value!.Value;

        book.PaymentAmount = "abc";
        Assert.Contains("amount", book.AddPayment());
        book.PaymentAmount = "10,000";
        book.PaymentKind = PaymentKind.Advance;
        book.PaymentMethod = PaymentMethod.Upi;
        book.PaymentReference = "UPI 4471";
        Assert.Null(book.AddPayment());

        var saved = vm.Store.Orders.ForProject(vm.Project.Id)!;
        Assert.Equal(10000m, saved.Paid);
        Assert.Equal(value - 10000m, saved.Balance);
        Assert.Equal("UPI 4471", saved.Payments.Single().Reference);
        Assert.Equal("", book.PaymentAmount);                                                // the form is ready for the next one
        Assert.Equal(PaymentKind.Stage, book.PaymentKind);
        Assert.Single(book.Payments);
        Assert.Contains("due", book.Orders.Single().PaidText);

        book.RemovePaymentCommand.Execute(book.Payments.Single());
        Assert.Equal(0m, vm.Store.Orders.ForProject(vm.Project.Id)!.Paid);
    }

    [Fact]
    public void TheNextPayment_FollowsTheBalance_AndMoreThanItIsAskedAndKeptAsCredit()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        var dialogs = (FakeDialogs)vm.Dialogs!;
        decimal value = vm.Store!.Orders.ForProject(vm.Project.Id)!.Value!.Value;

        Assert.Equal(PaymentKind.Advance, book.PaymentKind);                                // the first one
        book.PaymentAmount = "1000";
        Assert.Null(book.AddPayment());
        Assert.Equal(PaymentKind.Stage, book.PaymentKind);                                  // then stage payments …
        book.PaymentAmount = (value - 1000m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(PaymentKind.Final, book.PaymentKind);                                  // … and what clears it is the final one

        // More than the balance: asked first; "no" records nothing.
        book.PaymentAmount = (value + 4000m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        dialogs.ConfirmAnswer = false;
        Assert.Null(book.AddPayment());
        Assert.Contains("more than the balance", dialogs.Confirms.Last());
        Assert.Single(vm.Store.Orders.ForProject(vm.Project.Id)!.Payments);

        dialogs.ConfirmAnswer = true;
        Assert.Null(book.AddPayment());
        Assert.True(book.HasCredit);
        Assert.Contains("5,000.00", book.CreditText);                                       // 1,000 + value + 4,000 − value
        Assert.Contains("credit", book.CreditText);
    }

    [Fact]
    public void DispatchNotes_AreNumbered_CountWhatWent_AndTheLastOneMakesTheOrderDispatched()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        string first = Path.Combine(_folder, "dn1.pdf");

        book.DispatchLines[0].SendText = "2";
        book.DispatchLines[1].SendText = "0";
        book.Vehicle = "RJ14 GA 1234";
        Assert.Null(book.CreateDispatch(first));

        Assert.True(new FileInfo(first).Length > 1000);
        Assert.StartsWith("%PDF", File.ReadAllText(first)[..4]);
        var order = vm.Store!.Orders.ForProject(vm.Project.Id)!;
        Assert.Equal("DN-00001", order.Dispatches.Single().Number);
        Assert.Equal(2, order.DispatchedOf(vm.Project.Frames[0].Id));
        Assert.Equal(OrderStage.Confirmed, order.Stage);                                    // not everything has gone
        Assert.Equal("1", book.DispatchLines[0].SendText);                                   // what is left
        Assert.Equal("2 of 3 gone", book.DispatchLines[0].StatusText);

        book.DispatchLines[0].SendText = "2";
        Assert.Contains("Only 1 of W1", book.CreateDispatch(Path.Combine(_folder, "x.pdf")));

        book.DispatchLines[0].SendText = "1";
        book.DispatchLines[1].SendText = "1";
        Assert.Null(book.CreateDispatch(Path.Combine(_folder, "dn2.pdf")));
        order = vm.Store.Orders.ForProject(vm.Project.Id)!;
        Assert.Equal(new[] { "DN-00001", "DN-00002" }, order.Dispatches.Select(d => d.Number));
        Assert.Equal(OrderStage.Dispatched, order.Stage);
        Assert.False(book.HasDispatchLeft);
    }

    [Fact]
    public void TheSignOff_MakesTheOrderInstalled_AndItsCertificate()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        Assert.Equal("Mr. Rohit Sharma", book.SignedBy);                                    // the client, to start with
        book.SignedBy = "";
        Assert.Contains("who signed", book.SaveSignOff());

        book.SignedBy = "Rohit Sharma";
        book.InstalledBy = "Team A (Imran)";
        book.Remarks = "One handle to be replaced.";
        Assert.Null(book.SaveSignOff());

        var order = vm.Store!.Orders.ForProject(vm.Project.Id)!;
        Assert.Equal(OrderStage.Installed, order.Stage);
        Assert.Equal("Team A (Imran)", order.SignOff!.InstalledBy);
        Assert.Contains("Signed off on", book.SignOffText);
        string certificate = Path.Combine(_folder, "certificate.pdf");
        Assert.Null(book.Certificate(certificate));
        Assert.True(new FileInfo(certificate).Length > 1000);
    }

    [Fact]
    public void TheSchedule_ListsVisitsOfEveryOrder_AndADoneOneLeavesComingUp()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        book.VisitKind = VisitKind.Installation;
        book.VisitDate = DateTime.Today.AddDays(2);
        book.VisitTime = "10:30";
        book.VisitTeam = "Team A";
        Assert.Null(book.AddVisit());
        book.VisitKind = VisitKind.Delivery;
        book.VisitDate = DateTime.Today.AddDays(-1);
        Assert.Null(book.AddVisit());

        vm.ShowView(AppView.Schedule, AppArea.Orders);
        Assert.Equal(AppPage.Schedule, vm.Page);
        Assert.Equal(2, book.Schedule.Count);
        Assert.True(book.Schedule[0].IsLate);                                               // yesterday's delivery, not done
        Assert.Contains("1 overdue", book.ScheduleSummary);
        Assert.Contains("1 in the next 7 days", book.ScheduleSummary);

        book.ToggleVisitCommand.Execute(book.Schedule[0]);
        Assert.Single(book.Schedule);                                                        // done: not coming up
        book.ScheduleFilter = "Done";
        Assert.Equal("Delivery", book.Schedule.Single().KindText);

        book.OpenVisitOrderCommand.Execute(book.Schedule.Single());
        Assert.Equal(AppPage.Orders, vm.Page);
    }

    [Fact]
    public void Stages_FollowProduction_AndCanBeSetByHand()
    {
        var vm = Order();
        Assert.Null(vm.StartProduction());
        vm.ShowOrder();
        var book = vm.OrderBook;
        Assert.Equal("In production", book.StageText);                                      // production started

        // Every window ready in the workshop: Ready.
        var id = vm.Store!.Production.ForProject(vm.Project.Id)!.Value;
        var job = vm.Store.Production.Load(id);
        foreach (var frame in vm.Project.Frames)
            job.SetDone(frame.Id, ProductionStep.Ready, frame.Design.Quantity, frame.Design.Quantity);
        vm.Store.Production.Save(job);
        book.Reload();
        Assert.Equal("Ready", book.StageText);

        Assert.Null(book.SetStage(OrderStage.Closed));                                       // with a balance: asks, here yes
        Assert.Equal("Closed", book.StageText);
        Assert.Equal("Closed", book.Orders.Single().StageText);                              // stays in view until the list is read again
        book.Reload();
        Assert.Empty(book.Orders);                                                           // open orders only
        book.Filter = "Closed";
        Assert.Single(book.Orders);
        var history = vm.Store.Orders.ForProject(vm.Project.Id)!.History.Select(h => h.Stage);
        Assert.Equal(new[] { OrderStage.Confirmed, OrderStage.InProduction, OrderStage.Ready, OrderStage.Closed }, history);
    }

    [Fact]
    public void WithoutTheFeature_NothingChanges()
    {
        var vm = Order();
        vm.ShowOrder();
        var book = vm.OrderBook;
        book.Blocked = () => "Order management is not in your package.";
        book.PaymentAmount = "5000";

        Assert.Contains("not in your package", book.AddPayment());
        Assert.Empty(vm.Store!.Orders.ForProject(vm.Project.Id)!.Payments);
    }

    [Fact]
    public void Orders_SurviveReopening_TheDatabase()
    {
        var vm = Order();
        vm.ShowOrder();
        vm.OrderBook.PaymentAmount = "1500";
        Assert.Null(vm.OrderBook.AddPayment());

        var again = new SqliteOrderRepository(vm.Store!.Database);
        var order = again.List().Single();
        Assert.Equal(1500m, order.Paid);
        Assert.Equal(PaymentKind.Advance, order.Payments.Single().Kind);
        Assert.Equal("DN-00001", again.NextDispatchNumber());
        Assert.Equal("DN-00008", CustomerOrder.NextDispatchNumber(new[] { "DN-00002", "DN-00007", "X" }));
    }
}
