using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Mark.Core.Commands;
using Mark.Core.Models;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Licensing;
using Mark.Licensing.Api;
using Mark.Reports;

namespace Mark.Designer.ViewModels;

/// <summary>An earlier revision of the open quote: "R0 · 3 Oct 2026 · Ravi Shah · 1,20,000.00", with "Open a copy".</summary>
public sealed record RevisionRow(string Name, string When, string By, string Value, ICommand OpenCommand);

/// <summary>
/// Milestone 15, sales: enquiries and their quotes, the quotation PDF, quote revisions, converting a quote to an order,
/// and the sales charts on the dashboard.
/// </summary>
public partial class MainViewModel
{
    public EnquiriesViewModel Enquiries { get; private set; } = null!;
    public QuotationSetupViewModel QuotationSetup { get; private set; } = null!;
    public SalesChartsViewModel SalesCharts { get; private set; } = null!;

    /// <summary>The kept revisions of the open quote, oldest first.</summary>
    public ObservableCollection<RevisionRow> QuoteRevisions { get; } = new();

    public bool HasQuoteRevisions => QuoteRevisions.Count > 0;

    public ICommand ExportQuotationCommand { get; private set; } = null!;
    public ICommand ConvertToOrderCommand { get; private set; } = null!;
    public ICommand NewRevisionCommand { get; private set; } = null!;

    /// <summary>Opens a written document (the quotation PDF) with the computer's viewer; set by the app.</summary>
    public Action<string>? OpenDocument { get; set; }

    private void CreateSalesFeatures()
    {
        Enquiries = new EnquiriesViewModel(() => Store?.Enquiries, CreateQuoteFromEnquiry, OpenQuote, () => Dialogs, () => CurrentUser.DisplayName)
        {
            Blocked = () => Access.ReadOnlyMessage
        };
        QuotationSetup = new QuotationSetupViewModel(() => Store?.Settings, () => Dialogs, CompanyProfile);
        SalesCharts = new SalesChartsViewModel(() => Store?.Projects, () => Store?.Enquiries);
        ExportQuotationCommand = new RelayCommand(() => Report(ExportQuotation()));
        ConvertToOrderCommand = new RelayCommand(() => Report(ConvertToOrder()), () => HasStore && Project.Quote.OrderNumber.Length == 0);
        NewRevisionCommand = new RelayCommand(() => Report(NewRevision()), () => HasStore);
    }

    /// <summary>
    /// With a MARK account: the quotation details the MARK supplier set (empty when none yet); without an account: null,
    /// and the company fills them in itself.
    /// </summary>
    public QuotationProfile? CompanyProfile()
    {
        if (!Access.IsLicensed) return null;
        try
        {
            return Store?.Settings.LoadCompanyProfileJson() is { Length: > 0 } json
                ? System.Text.Json.JsonSerializer.Deserialize<QuotationProfile>(json, LicenceJson.Options) ?? QuotationProfile.Empty
                : QuotationProfile.Empty;
        }
        catch (Exception ex) when (ex is DataStoreException or System.Text.Json.JsonException)
        {
            return QuotationProfile.Empty;
        }
    }

    /// <summary>"Order OR-00003 · 4 Oct 2026", or "" before the quote became an order.</summary>
    public string OrderText => Project.Quote.OrderNumber.Length == 0 ? ""
        : $"Order {Project.Quote.OrderNumber}" + (Project.Quote.OrderedUtc is { } at ? $" · {at.ToLocalTime():d MMM yyyy}" : "");

    public bool IsOrder => Project.Quote.OrderNumber.Length > 0;

    // ── Enquiries ───────────────────────────────────────────────────

    /// <summary>
    /// A new quote for an enquiry: its client, site and requirements, linked to it; the enquiry becomes "Quoted". The
    /// quote opens on its Client tab. Returns an error or null.
    /// </summary>
    public string? CreateQuoteFromEnquiry(Enquiry enquiry)
    {
        ArgumentNullException.ThrowIfNull(enquiry);
        if (Access.ReadOnlyMessage is { } readOnly) return readOnly;
        if (!ConfirmDiscardChanges()) return null;
        var project = new Project
        {
            Name = enquiry.Client.DisplayName.Length > 0 ? enquiry.Client.DisplayName : $"Enquiry {enquiry.Number}",
            Pricing = DefaultPricing()
        };
        project.Quote.Client = enquiry.Client.Copy();
        project.Quote.Notes = enquiry.Requirements;
        project.Quote.EnquiryId = enquiry.Id;
        ShowProject(project);
        enquiry.QuoteId = project.Id;
        if (enquiry.Stage is EnquiryStage.New or EnquiryStage.Contacted or EnquiryStage.SiteVisit) enquiry.Stage = EnquiryStage.Quoted;
        try
        {
            Store?.Enquiries.Save(enquiry);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        ShowView(AppView.Client, AppArea.Sales);
        Hint = $"New quote for enquiry {enquiry.Number}. Add the designs, then save.";
        HintIsError = false;
        return null;
    }

    /// <summary>After a save: the enquiry the quote came from follows its result (won, lost, quoted).</summary>
    private void SyncEnquiry()
    {
        if (Store is null || Project.Quote.EnquiryId is not { } id) return;
        try
        {
            var enquiry = Store.Enquiries.Load(id);
            var stage = Project.Quote.Status switch
            {
                QuoteStatus.Won => EnquiryStage.Won,
                QuoteStatus.Lost => EnquiryStage.Lost,
                _ => EnquiryStage.Quoted
            };
            if (enquiry.Stage == stage && enquiry.QuoteId == Project.Id) return;
            enquiry.Stage = stage;
            enquiry.QuoteId = Project.Id;
            Store.Enquiries.Save(enquiry);
        }
        catch (DataStoreException)
        {
            // The enquiry was deleted or cannot be read: the quote is saved all the same.
        }
    }

    // ── Quotation PDF ───────────────────────────────────────────────

    /// <summary>The quotation of the open quote, with the company's setup.</summary>
    public QuotationDocument BuildQuotation()
    {
        QuotationSetup.SaveIfChanged();                                  // the setup as typed, even without Save setup
        QuotationSettings settings;
        try
        {
            settings = Store?.Settings.LoadQuotationSettings() ?? new QuotationSettings();
        }
        catch (DataStoreException)
        {
            settings = new QuotationSettings();
        }
        if (CompanyProfile() is { } profile) settings = QuotationBuilder.WithProfile(settings, profile);
        string company = Access.IsLicensed && Access.CompanyName.Length > 0 ? Access.CompanyName
            : settings.CompanyName.Length > 0 ? settings.CompanyName : "Your company";
        byte[]? logo = null;
        try
        {
            if (Access.Status?.Licence.LogoBase64 is { Length: > 0 } base64) logo = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            // Without a readable logo the header shows the name only.
        }
        return QuotationBuilder.Build(new QuotationInputs(Project, Calculation.Result, Price, Library, Rules, settings, company, logo, DateTime.Now)
            ) with { QuoteNumber = Project.Quote.NumberText };
    }

    /// <summary>
    /// Writes the open quote as a quotation PDF (to <paramref name="path"/>, or where the user chooses) and opens it.
    /// The quote must be saved, so the quotation carries its number and saved value. Returns an error, or null.
    /// </summary>
    public string? ExportQuotation(string? path = null)
    {
        if (!Access.Allows(Features.QuotationPdf)) return Access.Lock(Features.QuotationPdf);
        if (Project.Frames.Count == 0) return "Add at least one design before making the quotation.";
        if (Store is not null && (IsDirty || !Store.Projects.Exists(Project.Id)))
            return "Save the quote first, so the quotation shows its number and the saved value.";
        string fileName = string.Join("_", $"{Project.Quote.NumberText} {Project.Name} Quotation".Split(Path.GetInvalidFileNameChars())).Trim() + ".pdf";
        path ??= Dialogs?.ChooseSaveFile("Save quotation", "PDF files (*.pdf)|*.pdf", fileName);
        if (path is null) return null;
        try
        {
            var document = BuildQuotation();
            using (var stream = File.Create(path))
                QuotationPdf.Write(document, stream);
            Hint = $"Saved the quotation {Path.GetFileName(path)}.";
            HintIsError = false;
            OpenDocument?.Invoke(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"The quotation could not be written: {ex.Message}";
        }
    }

    // ── Orders and revisions ────────────────────────────────────────

    /// <summary>
    /// Turns the saved quote into an order: it is marked won and gets the next order number (OR-00001…), and is saved.
    /// Returns an error, or null.
    /// </summary>
    public string? ConvertToOrder()
    {
        if (Store is null) return "There is no local database.";
        if (Access.ReadOnlyMessage is { } readOnly) return readOnly;
        if (IsOrder) return $"This quote is already order {Project.Quote.OrderNumber}.";
        if (Project.Frames.Count == 0) return "Add the designs before converting the quote to an order.";
        if (IsDirty || !Store.Projects.Exists(Project.Id)) return "Save the quote first.";
        if (Dialogs is not null && !Dialogs.Confirm("Convert to order",
                $"Convert {Project.Quote.NumberText} '{Project.Name}' to an order? It is marked won and gets an order number."))
            return null;
        var quote = Project.Quote.Copy();
        quote.Status = QuoteStatus.Won;
        quote.OrderedUtc = DateTime.UtcNow;
        if (RunForMessage(() => new SetQuoteInfoCommand(Project, Project.Name, quote)) is { } error) return error;
        string? saved = Persist($"Converted to an order.");
        if (saved is not null) return saved;
        Hint = $"{Project.Quote.NumberText} is now order {Project.Quote.OrderNumber}.";
        OnPropertyChanged(nameof(OrderText));
        OnPropertyChanged(nameof(IsOrder));
        ((RelayCommand)ConvertToOrderCommand).RaiseCanExecuteChanged();
        return null;
    }

    /// <summary>
    /// Keeps the saved quote as its current revision and continues as the next one (QT-00012 → QT-00012 R1), saved at
    /// once. Earlier revisions stay listed on the Client tab and can be opened as a copy. Returns an error, or null.
    /// </summary>
    public string? NewRevision()
    {
        if (Store is null) return "There is no local database.";
        if (Access.ReadOnlyMessage is { } readOnly) return readOnly;
        if (IsDirty || !Store.Projects.Exists(Project.Id)) return "Save the quote first; the saved version is kept as the revision.";
        try
        {
            Store.Projects.KeepRevision(Project.Id);
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
        var quote = Project.Quote.Copy();
        quote.Revision = Project.Quote.Revision + 1;
        if (RunForMessage(() => new SetQuoteInfoCommand(Project, Project.Name, quote)) is { } error) return error;
        CommandHistory.Clear();                                   // the kept revision cannot be undone into
        string? saved = Persist($"Started revision R{quote.Revision}.");
        if (saved is not null) return saved;
        Hint = $"Started revision R{quote.Revision}. R{quote.Revision - 1} is kept and listed on the Client tab.";
        return null;
    }

    /// <summary>Opens a kept revision as a new, unsaved quote (new ids, no number). Returns an error or null.</summary>
    public string? OpenRevisionCopy(int revision)
    {
        if (Store is null) return "There is no local database.";
        if (!ConfirmDiscardChanges()) return null;
        try
        {
            var kept = Store.Projects.LoadRevision(Project.Id, revision);
            var copy = kept.Clone();
            copy.Name = $"{kept.Name} (R{revision} copy)";
            copy.Quote.Number = "";
            copy.Quote.Revision = 0;
            copy.Quote.OrderNumber = "";
            copy.Quote.OrderedUtc = null;
            copy.Quote.Status = QuoteStatus.Active;
            ShowProject(copy);
            ShowView(AppView.Client, AppArea.Sales);
            Hint = $"Opened a copy of R{revision} as a new quote. Save it to keep it.";
            HintIsError = false;
            return null;
        }
        catch (DataStoreException ex)
        {
            return ex.Message;
        }
    }

    private void RefreshRevisions()
    {
        QuoteRevisions.Clear();
        if (Store is not null)
        {
            try
            {
                foreach (var r in Store.Projects.Revisions(Project.Id))
                {
                    int number = r.Revision;
                    QuoteRevisions.Add(new RevisionRow(r.Name, r.SavedUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture),
                        r.SavedBy, r.Value is { } v ? $"{v.ToString("N2", CultureInfo.InvariantCulture)} {r.Currency}".Trim() : "—",
                        new RelayCommand(() => Report(OpenRevisionCopy(number)))));
                }
            }
            catch (DataStoreException)
            {
                // No revisions to show.
            }
        }
        OnPropertyChanged(nameof(HasQuoteRevisions));
        OnPropertyChanged(nameof(OrderText));
        OnPropertyChanged(nameof(IsOrder));
        (ConvertToOrderCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
