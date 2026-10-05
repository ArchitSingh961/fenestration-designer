using System.IO;
using System.Xml.Linq;
using Mark.Core.Accounts;
using Mark.Core.Commands;
using Mark.Core.Models;
using Mark.Core.Orders;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Mark.Tests.Sales;
using Xunit;

namespace Mark.Tests.Accounts;

/// <summary>
/// Milestone 19, accounts: GST basics, tax invoices from orders (CGST + SGST or IGST, numbering, partial invoices,
/// cancelling), receipts, what clients owe, and the exports for Excel and Tally.
/// </summary>
public class AccountsTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Gstin(string first14) => first14 + Gst.CheckCharacter(first14);

    // ── GST basics ──────────────────────────────────────────────────

    [Fact]
    public void Gstins_AreChecked()
    {
        Assert.True(Gst.IsGstin("27AAPFU0939F1ZV"));                     // the example in the GST documentation
        Assert.False(Gst.IsGstin("27AAPFU0939F1ZW"));                    // wrong check character
        Assert.False(Gst.IsGstin("99AAPFU0939F1ZV"));                    // no such state
        Assert.False(Gst.IsGstin("27AAPFU0939F1Z"));
        Assert.True(Gst.IsGstin(Gstin("08ABCDE1234F1Z")));
        Assert.Equal("08", Gst.StateCodeOfGstin("08ABCDE1234F1Z0"));
        Assert.Equal("08", Gst.StateCodeOfName("rajasthan"));
        Assert.Equal("07", Gst.StateCodeOfName("New Delhi"));
        Assert.Equal("26", Gst.StateCodeOfName("Dadra & Nagar Haveli and Daman & Diu"));
        Assert.Null(Gst.StateCodeOfName("Atlantis"));
    }

    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(11800, "Rupees Eleven Thousand Eight Hundred Only")]
    [InlineData(743566.65, "Rupees Seven Lakh Forty Three Thousand Five Hundred Sixty Six and Sixty Five Paise Only")]
    [InlineData(12500000, "Rupees One Crore Twenty Five Lakh Only")]
    [InlineData(101, "Rupees One Hundred One Only")]
    public void Amounts_AreWrittenInWords(decimal amount, string words) => Assert.Equal(words, Gst.AmountInWords(amount));

    [Fact]
    public void FinancialYears_RunFromApril()
    {
        Assert.Equal("2026-27", Gst.FinancialYear(new DateTime(2026, 4, 1)));
        Assert.Equal("2025-26", Gst.FinancialYear(new DateTime(2026, 3, 31)));
        Assert.Equal("INV/2026-27/0001", Invoice.NextNumber("INV", true, new DateTime(2026, 10, 6), new[] { "INV/2025-26/0040" }));
        Assert.Equal("INV/2026-27/0013", Invoice.NextNumber("INV", true, new DateTime(2026, 10, 6), new[] { "INV/2026-27/0012", "INV-00099" }));
        Assert.Equal("INV-00100", Invoice.NextNumber("INV", false, DateTime.Today, new[] { "INV-00099" }));
    }

    [Fact]
    public void TheTax_IsSplitWithinTheState_AndRounded()
    {
        var invoice = new Invoice
        {
            SellerState = "08", PlaceOfSupply = "08", TaxPercent = 18,
            Lines = { new InvoiceLine("a", "W1", "7610", 3, "Nos", 4586.43m), new InvoiceLine("b", "Transport", "", 1, "Job", 1000) }
        };
        Assert.Equal(14759.29m, invoice.Taxable);
        Assert.True(invoice.IsIntraState);
        Assert.Equal(1328.34m, invoice.Cgst);
        Assert.Equal(invoice.Cgst, invoice.Sgst);
        Assert.Equal(0, invoice.Igst);
        Assert.Equal(17416m, invoice.Total);                             // rounded to the rupee
        Assert.Equal(0.03m, invoice.RoundOff);

        invoice.PlaceOfSupply = "27";
        Assert.False(invoice.IsIntraState);
        Assert.Equal(2656.67m, invoice.Igst);
        Assert.Equal(0, invoice.Cgst);
    }

    // ── From an order ───────────────────────────────────────────────

    /// <summary>A confirmed order for a client in <paramref name="state"/>: W1 1200 × 1500 × 3 and W2 900 × 1200; the company is in Rajasthan.</summary>
    private (MainViewModel Vm, LocalStore Store, FakeDialogs Dialogs, CustomerOrder Order) Ordered(string state = "Rajasthan", string gstin = "")
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        store.Settings.SaveQuotationSettings(new QuotationSettings { CompanyName = "Test Windows", Gstin = Gstin("08ABCDE1234F1Z"), BankName = "Sample Bank" });
        var dialogs = new FakeDialogs { PromptAnswer = "Verma house" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.Project.Name = "Verma house";
        vm.Project.Quote.Client = new ClientInfo { FirstName = "Neha", LastName = "Verma", City = "Jaipur", State = state, Gstin = gstin };
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Quantity = 3;
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 900, 1200, vm.Rules));
        vm.Project.Frames[1].Design.Reference = "W2";
        Assert.Null(vm.SaveProject());
        Assert.Null(vm.ConvertToOrder());
        Assert.Null(vm.ShowOrder());
        var order = store.Orders.ForProject(vm.Project.Id)!;
        return (vm, store, dialogs, order);
    }

    [Fact]
    public void AnOrder_IsInvoicedAtItsPrices_WithCgstAndSgstWithinTheState()
    {
        var (vm, store, _, order) = Ordered();
        vm.ShowView(AppView.Invoices);
        var acc = vm.Accounts;
        acc.OrderToInvoice = acc.OrderChoices.Single();
        Assert.Null(acc.MakeDraft());

        Assert.True(acc.HasDraft);
        Assert.Equal(4, acc.DraftLines.Count);                          // W1, W2, transport, loading
        Assert.Equal("7610", acc.DraftLines[0].Hsn);                    // aluminium
        Assert.Equal("Rajasthan", acc.PlaceOfSupply!.Name);
        Assert.Equal("", acc.DraftWarning);
        Assert.Contains("CGST 9 %", acc.DraftTotals);

        string pdf = Path.Combine(_temp.Folder, "inv.pdf");
        Assert.Null(acc.CreateInvoice(pdf));
        var invoice = store.Invoices.List().Single();
        Assert.Matches(@"^INV/\d{4}-\d{2}/0001$", invoice.Number);
        Assert.True(invoice.IsIntraState);
        // The invoice adds up to the order's value (to the rupee).
        Assert.Equal(Math.Round(order.Value!.Value, 0, MidpointRounding.AwayFromZero), invoice.Total);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
        Assert.Contains("Everything of", acc.MakeDraft());
    }

    [Fact]
    public void APartInvoice_LeavesTheRestForTheNext_AndACancelledOneFreesItsLines()
    {
        var (vm, store, dialogs, _) = Ordered(state: "Maharashtra", gstin: Gstin("27ABCDE1234F1Z"));
        vm.ShowView(AppView.Invoices);
        var acc = vm.Accounts;
        acc.OrderToInvoice = acc.OrderChoices.Single();
        Assert.Null(acc.MakeDraft());
        Assert.Equal("Maharashtra", acc.PlaceOfSupply!.Name);
        Assert.Contains("IGST 18 %", acc.DraftTotals);

        acc.DraftLines[0].QuantityText = "5";
        Assert.Contains("Only 3", acc.CreateInvoice(Path.Combine(_temp.Folder, "x.pdf")));
        acc.DraftLines[0].QuantityText = "2";                            // two of the three W1 now
        acc.DraftLines[1].QuantityText = "0";                            // W2 later
        Assert.Null(acc.CreateInvoice(Path.Combine(_temp.Folder, "1.pdf")));
        var first = store.Invoices.List().Single();
        Assert.True(first.IsB2B);
        Assert.Equal(first.Taxable * 0.18m, first.Igst, 2);

        Assert.Null(acc.MakeDraft());
        Assert.Equal(new[] { 1.0, 1.0 }, acc.DraftLines.Select(l => l.Left));   // W1 × 1 and W2 × 1 left; charges done
        Assert.Null(acc.CreateInvoice(Path.Combine(_temp.Folder, "2.pdf")));
        Assert.EndsWith("0002", store.Invoices.List().First().Number);

        dialogs.PromptAnswer = "Wrong rate";
        acc.SelectedInvoice = acc.Invoices.Single(r => r.Invoice.Id == first.Id);
        Assert.Null(acc.CancelInvoice());
        Assert.True(store.Invoices.List().Single(i => i.Id == first.Id).IsCancelled);
        Assert.Null(acc.MakeDraft());                                    // what it had can be invoiced again
        Assert.Contains(acc.DraftLines, l => l.Left == 2);
    }

    [Fact]
    public void Receipts_GetANumberOnce_AndOutstandingShowsWhatIsDue()
    {
        var (vm, store, _, order) = Ordered();
        order.Payments.Add(new OrderPayment { Date = DateTime.Today, Amount = 10000, Method = PaymentMethod.Upi, Reference = "UPI-1" });
        store.Orders.Save(order);
        vm.ShowView(AppView.Invoices);
        var acc = vm.Accounts;
        acc.OrderToInvoice = acc.OrderChoices.Single();
        Assert.Null(acc.MakeDraft());
        Assert.Null(acc.CreateInvoice(Path.Combine(_temp.Folder, "i.pdf")));
        var invoice = store.Invoices.List().Single();

        vm.ShowView(AppView.Receipts);
        var row = acc.Receipts.Single();
        string pdf = Path.Combine(_temp.Folder, "r.pdf");
        Assert.Null(acc.ReceiptPdf(row.Order.Id, row.Payment.Id, pdf));
        Assert.True(File.Exists(pdf));
        Assert.Equal("RCPT-00001", store.Orders.Load(order.Id).Payments.Single().ReceiptNumber);
        Assert.Null(acc.ReceiptPdf(row.Order.Id, row.Payment.Id, pdf));             // the same number again
        Assert.Equal("RCPT-00001", acc.Receipts.Single().Number);

        vm.ShowView(AppView.Outstanding);
        var owed = acc.Outstanding.Single();
        Assert.True(owed.IsDue);
        Assert.Equal($"₹ {(invoice.Total - 10000).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))}", owed.Due);
        Assert.StartsWith(invoice.Number, owed.Oldest);
    }

    [Fact]
    public void ChangedPrices_CanBeBroughtBackToTheOrdersValue()
    {
        var (vm, store, _, order) = Ordered();
        order.Value = order.Value!.Value * 1.1m;                         // confirmed at a higher price than today's
        store.Orders.Save(order);
        vm.ShowView(AppView.Invoices);
        var acc = vm.Accounts;
        acc.OrderToInvoice = acc.OrderChoices.Single();
        Assert.Null(acc.MakeDraft());
        Assert.True(acc.HasPriceDifference);
        Assert.Contains("confirmed at", acc.DraftWarning);

        string transport = acc.DraftLines.Single(l => l.Description == "Transportation Cost").RateText;
        acc.MatchOrderValue();
        Assert.False(acc.HasPriceDifference);
        Assert.Equal(transport, acc.DraftLines.Single(l => l.Description == "Transportation Cost").RateText);   // charges stay
        Assert.DoesNotContain("confirmed at", acc.DraftWarning);
        Assert.Null(acc.CreateInvoice(Path.Combine(_temp.Folder, "m.pdf")));
        Assert.Equal(order.Value!.Value, store.Invoices.List().Single().Total, 0);   // within a rupee or so of the agreed value
    }

    // ── Exports ─────────────────────────────────────────────────────

    [Fact]
    public void TheExports_AreValidAndBalance()
    {
        var invoice = new Invoice
        {
            Number = "INV/2026-27/0001", Date = new DateTime(2026, 10, 6), ClientName = "Verma, \"Neha\"", ClientGstin = Gstin("08ABCDE1234F1Z"),
            SellerState = "08", PlaceOfSupply = "08", TaxPercent = 18, OrderNumber = "OR-00001",
            Lines = { new InvoiceLine("a", "W1 · 1200 × 1500", "7610", 3, "Nos", 1000), new InvoiceLine("b", "=cmd", "", 1, "Job", 500.5m) }
        };
        var receipt = new ReceiptEntry(new DateTime(2026, 10, 7), "RCPT-00001", invoice.ClientName, "OR-00001", 2000, "Cash", "", true);

        string csv = AccountsExport.InvoicesCsv(new[] { invoice });
        Assert.Contains("\"Verma, \"\"Neha\"\"\"", csv);                 // quoted for Excel
        Assert.Contains(invoice.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), csv);
        Assert.Contains("'=cmd", AccountsExport.HsnSummaryCsv(new[] { invoice }));      // not read by Excel as a formula
        Assert.Contains("RCPT-00001", AccountsExport.ReceiptsCsv(new[] { receipt }));

        var xml = XDocument.Parse(AccountsExport.TallyXml(new[] { invoice }, new[] { receipt }, new AccountsSettings()));
        var vouchers = xml.Descendants("VOUCHER").ToList();
        Assert.Equal(new[] { "Sales", "Receipt" }, vouchers.Select(v => (string)v.Attribute("VCHTYPE")!));
        foreach (var v in vouchers)                                              // every voucher balances
            Assert.Equal(0m, v.Descendants("AMOUNT").Sum(a => decimal.Parse(a.Value, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Contains(xml.Descendants("LEDGER"), l => (string)l.Attribute("NAME")! == invoice.ClientName);
        Assert.Contains(xml.Descendants("LEDGERNAME"), l => l.Value == "Output CGST");
    }

    [Fact]
    public void TheSetup_IsSavedAndChecked()
    {
        var (vm, store, _, _) = Ordered();
        vm.ShowView(AppView.AccountsExport);
        var acc = vm.Accounts;
        Assert.Equal("INV", acc.Prefix);
        acc.Prefix = "GW";
        acc.ByYear = false;
        acc.HsnUpvc = "39252000";
        Assert.Null(acc.SaveSettings());
        Assert.Equal("GW", store.Settings.LoadAccountsSettings().InvoicePrefix);
        Assert.Equal("Next invoice: GW-00001", acc.NextInvoiceText);
        acc.Prefix = "G W";
        Assert.NotNull(acc.SaveSettings());
        acc.Prefix = "GW";
        acc.SalesLedger = " ";
        Assert.Contains("Tally ledger", acc.SaveSettings());

        string path = Path.Combine(_temp.Folder, "t.xml");
        Assert.Null(acc.Export("tally", path));
        Assert.NotNull(XDocument.Load(path));
        acc.FromText = "bad";
        Assert.Contains("period", acc.Export("invoices", path));
    }

    [Fact]
    public void AClientGstin_IsChecked()
    {
        var project = new Project();
        var bad = new QuoteInfo { Client = new ClientInfo { FirstName = "A", Gstin = "08ABCDE1234F1Z9" } };
        Assert.False(QuoteEditor.TrySetQuote(project, "P", bad).Success);
        var good = new QuoteInfo { Client = new ClientInfo { FirstName = "A", Gstin = Gstin("08abcde1234f1z".ToUpperInvariant()).ToLowerInvariant() } };
        Assert.True(QuoteEditor.TrySetQuote(project, "P", good).Success);
        Assert.Equal(good.Client.Gstin.ToUpperInvariant(), project.Quote.Client.Gstin);
    }
}
