using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>Enquiries and quotes in one list: every enquiry (with its quote's value) and every quote of its own.</summary>
public class EnquiriesAndQuotesTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void OneList_HasEnquiriesAndQuotes_AndOpensEach()
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var dialogs = new FakeDialogs { PromptAnswer = "Office block" };
        var vm = new MainViewModel(store, null, dialogs);

        // A quote of its own …
        vm.CreateFrame();
        Assert.Null(vm.SaveProject());
        var quoteId = vm.Project.Id;
        // … and an enquiry with a quote made from it.
        var enquiry = new Enquiry { Client = new ClientInfo { FirstName = "Neha" }, Source = "Website", Owner = "Ravi" };
        store.Enquiries.Save(enquiry);
        Assert.Null(vm.CreateQuoteFromEnquiry(store.Enquiries.Load(enquiry.Id)));
        vm.CreateFrame();
        Assert.Null(vm.SaveProject());

        vm.ShowView(AppView.Enquiries);
        var list = vm.Enquiries;
        list.Filter = EnquiryFilter.All;
        Assert.Equal(2, list.Rows.Count);                                                    // not three: the enquiry's quote is on its row
        var own = list.Rows.Single(r => r.QuoteOnly);
        var fromEnquiry = list.Rows.Single(r => !r.QuoteOnly);
        Assert.Equal(("Quote", "Enquiry"), (own.Kind, fromEnquiry.Kind));
        Assert.NotEqual("", fromEnquiry.Value);                                              // the quote's value
        Assert.Equal("Quoted", own.Stage);
        list.Filter = EnquiryFilter.Quoted;
        Assert.Equal(2, list.Rows.Count);

        list.EditCommand.Execute(own);                                                       // a quote opens as a quote
        Assert.Equal(quoteId, vm.Project.Id);
        Assert.Equal(AppPage.Quote, vm.Page);

        vm.ShowView(AppView.Enquiries);
        list.Filter = EnquiryFilter.All;
        list.DeleteQuoteCommand.Execute(list.Rows.Single(r => r.QuoteOnly));                 // the open quote is not deleted
        Assert.Contains("open", list.Message);
        Assert.Equal("Enquiries & quotes", vm.Tabs.Single(t => t.IsSelected).Title);
    }
}
