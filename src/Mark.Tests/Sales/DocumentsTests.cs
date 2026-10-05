using System.IO;
using Mark.Core.Commands;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Designer.ViewModels;
using Mark.Tests.Data;
using Xunit;

namespace Mark.Tests.Sales;

/// <summary>
/// The quote's Documents tab: files kept with the quote by category, upload, search and sort, open / save a copy /
/// delete, quotations kept when written, the margin report, and the designs of each saved revision.
/// </summary>
public class DocumentsTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private (MainViewModel Vm, LocalStore Store, FakeDialogs Dialogs) Quote(bool save = true)
    {
        var store = _temp.Open(TempDatabase.ShippedLibraryPath);
        var dialogs = new FakeDialogs { PromptAnswer = "Mehta house" };
        var vm = new MainViewModel(store, null, dialogs);
        vm.Project.Name = "Mehta house";
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 0, 0, 1200, 1500, vm.Rules));
        vm.Project.Frames[0].Design.Reference = "W1";
        vm.Project.Frames[0].Design.Quantity = 2;
        if (save) Assert.Null(vm.SaveProject());
        return (vm, store, dialogs);
    }

    private string File(string name, int bytes)
    {
        string path = Path.Combine(_temp.Folder, name);
        System.IO.File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        return path;
    }

    [Fact]
    public void TheRepository_KeepsFilesWithTheirQuote()
    {
        var (vm, store, _) = Quote();
        var other = Guid.NewGuid();
        byte[] content = { 1, 2, 3, 4 };
        var kept = store.Documents.Add(new ProjectDocument(Guid.NewGuid(), vm.Project.Id, DocumentCategory.CreditApproval, "Credit form",
            "credit.pdf", DateTime.UtcNow, "Ravi", 0), content);
        store.Documents.Add(new ProjectDocument(Guid.NewGuid(), other, DocumentCategory.Others, "Not mine", "x.txt", DateTime.UtcNow, "Ravi", 0), content);

        var mine = store.Documents.ForProject(vm.Project.Id).Single();
        Assert.Equal(kept, mine);
        Assert.Equal(4, mine.Size);
        Assert.Equal(content, store.Documents.Content(mine.Id));
        store.Documents.Delete(mine.Id);
        Assert.Empty(store.Documents.ForProject(vm.Project.Id));
        Assert.Throws<DataStoreException>(() => store.Documents.Content(mine.Id));
        Assert.Throws<DataStoreException>(() => store.Documents.Add(mine, new byte[ProjectDocument.MaxSize + 1]));
    }

    [Fact]
    public void Upload_SearchSortOpenSaveAndDelete()
    {
        var (vm, _, dialogs) = Quote();
        vm.ShowView(AppView.Documents);
        var docs = vm.QuoteDocuments;
        Assert.Equal(QuoteSection.Documents, vm.Section);
        Assert.Equal(7, docs.Categories.Count);
        Assert.Equal("Pre Production Survey Report", docs.CategoryTitle);
        Assert.True(docs.IsEmpty);

        Assert.Null(docs.Upload(File("site survey.pdf", 300)));
        Assert.Null(docs.Upload(File("Photos.jpg", 3000)));
        Assert.Equal(new[] { "Photos", "site survey" }, docs.Documents.Select(d => d.Name));       // newest first
        Assert.Equal(2, docs.Categories[0].Count);
        Assert.Equal("JPG", docs.Documents[0].Kind);

        docs.Sort = "Name A–Z";
        Assert.Equal(new[] { "Photos", "site survey" }, docs.Documents.Select(d => d.Name));
        docs.Sort = "Largest first";
        Assert.Equal("Photos", docs.Documents[0].Name);
        docs.Search = "survey";
        Assert.Equal("site survey", docs.Documents.Single().Name);
        docs.Search = "";

        // Each category has its own files.
        docs.Select(DocumentCategory.SalesOrder);
        Assert.True(docs.IsEmpty);
        Assert.Null(docs.Upload(File("contract.pdf", 10)));
        Assert.Equal("contract", docs.Documents.Single().Name);
        Assert.Equal(1, docs.Categories.Single(c => c.Category == DocumentCategory.SalesOrder).Count);

        string? opened = null;
        docs.OpenDocument = p => opened = p;
        docs.Documents.Single().OpenCommand.Execute(null);
        Assert.NotNull(opened);
        Assert.Equal(10, new FileInfo(opened!).Length);

        string copy = Path.Combine(_temp.Folder, "copy.pdf");
        dialogs.SaveFileAnswer = copy;
        docs.Documents.Single().SaveCommand.Execute(null);
        Assert.Equal(10, new FileInfo(copy).Length);

        docs.Documents.Single().DeleteCommand.Execute(null);
        Assert.Contains("contract", dialogs.Confirms.Last());
        Assert.True(docs.IsEmpty);
    }

    [Fact]
    public void AnUnsavedQuote_HasNoDocumentsYet()
    {
        var (vm, _, _) = Quote(save: false);
        vm.ShowView(AppView.Documents);
        Assert.False(vm.QuoteDocuments.IsAvailable);
        Assert.StartsWith("Save the quote first", vm.QuoteDocuments.Upload(File("a.pdf", 5)));
    }

    [Fact]
    public void AWrittenQuotation_IsKeptUnderQuotations()
    {
        var (vm, store, _) = Quote();
        vm.OpenDocument = _ => { };
        string path = Path.Combine(_temp.Folder, "quotation.pdf");

        Assert.Null(QuotationPdfTests.OnSta(() => vm.ExportQuotation(path)));

        var kept = store.Documents.ForProject(vm.Project.Id).Single();
        Assert.Equal(DocumentCategory.Quotations, kept.Category);
        Assert.Equal("Quotation QT-00001", kept.Name);
        Assert.Equal(System.IO.File.ReadAllBytes(path), store.Documents.Content(kept.Id));
    }

    [Fact]
    public void TheMarginReport_ShowsCostPriceAndMargin()
    {
        var (vm, store, _) = Quote();
        var report = MarginBuilder.Build(vm.Project, vm.Price, "Test Windows", new DateTime(2026, 10, 6));
        var design = report.Designs.Single();
        var price = vm.Price.Designs.Single();
        decimal profit = price.Heads.Single(h => h.Name == "Profit").Amount;

        // Cost is the basic value without the profit; the margin is the profit (no discount).
        Assert.Equal(((price.UnitBasicPrice - profit) * 2).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN")), design.Cost);
        Assert.Equal((profit * 2).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN")), design.Margin);
        Assert.Equal("Profile Cost", design.Lines[0].Name);
        Assert.Contains(report.Totals, t => t.Label == "Margin on price");

        vm.ShowView(AppView.Documents);
        vm.QuoteDocuments.Select(DocumentCategory.Margins);
        Assert.True(vm.QuoteDocuments.IsMargins);
        Assert.Null(vm.QuoteDocuments.MakeMargins());
        var kept = store.Documents.ForProject(vm.Project.Id).Single();
        Assert.Equal(DocumentCategory.Margins, kept.Category);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(store.Documents.Content(kept.Id), 0, 4));
    }

    [Fact]
    public void TypologyHistory_ListsTheDesignsOfEachRevision()
    {
        var (vm, _, _) = Quote();
        Assert.Null(vm.NewRevision());
        vm.CommandHistory.Execute(CreateFrameCommand.Create(vm.Project, 2000, 0, 900, 1200, vm.Rules));
        vm.Project.Frames[1].Design.Reference = "W2";
        Assert.Null(vm.SaveProject());

        vm.ShowView(AppView.Documents);
        vm.QuoteDocuments.Select(DocumentCategory.TypologyHistory);
        var rows = vm.QuoteDocuments.Typologies;
        Assert.Equal("Now", rows[0].Revision);
        Assert.Contains("W2 900 × 1200", rows[0].Designs);
        Assert.Contains(rows, r => r.Revision == "R0" && r.Designs == "W1 1200 × 1500 ×2");
    }
}
