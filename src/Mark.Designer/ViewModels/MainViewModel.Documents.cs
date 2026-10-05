using Mark.Core.Quotes;

namespace Mark.Designer.ViewModels;

/// <summary>The open quote's Documents tab: files kept with the quote, the quotations MARK wrote, margin reports.</summary>
public partial class MainViewModel
{
    public DocumentsViewModel QuoteDocuments { get; private set; } = null!;

    private void CreateDocumentFeatures()
    {
        QuoteDocuments = new DocumentsViewModel(() => Store, () => Project,
            () => Store is not null && Store.Projects.Exists(Project.Id),
            () => Dialogs, () => Access.UserName, () => Access.ReadOnlyMessage,
            () => MarginBuilder.Build(Project, Price, Access.IsLicensed && Access.CompanyName.Length > 0 ? Access.CompanyName : "Your company", DateTime.Now));
    }

    /// <summary>Keeps a copy of a quotation MARK just wrote under the quote's Quotations (quietly: the PDF itself was written).</summary>
    private void KeepQuotation(string path)
    {
        try
        {
            byte[] content = System.IO.File.ReadAllBytes(path);
            QuoteDocuments.Keep(DocumentCategory.Quotations, $"Quotation {Project.Quote.NumberText}".Trim(), System.IO.Path.GetFileName(path), content);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // The quotation is on disk where the user saved it; only the copy in Documents is missing.
        }
    }

    private void RefreshDocumentsIfShown()
    {
        if (_page == AppPage.Quote && _section == QuoteSection.Documents)
        {
            QuoteDocuments.OpenDocument = OpenDocument;
            QuoteDocuments.Reload();
        }
    }
}
