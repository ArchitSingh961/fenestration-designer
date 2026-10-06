using System.Collections.ObjectModel;
using System.Windows.Input;
using Mark.Core.Accounts;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>A step of the Get started checklist on the dashboard.</summary>
public sealed record SetupStep(string Title, string Detail, bool IsDone, string ActionText, ICommand Action)
{
    public bool IsOpen => !IsDone;
}

/// <summary>
/// Get started: what a new company sets up before its first quotation and invoice (company details with GSTIN, its
/// prices, its default pricing, the first quote), shown on the dashboard until everything is done or it is hidden.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<SetupStep> SetupSteps { get; } = new();

    private bool _showSetup;
    /// <summary>The checklist is shown: something is not set up yet, and it was not hidden.</summary>
    public bool ShowSetup { get => _showSetup; private set => SetProperty(ref _showSetup, value); }

    public string SetupProgressText
    {
        get
        {
            int done = SetupSteps.Count(s => s.IsDone);
            return $"{done} of {SetupSteps.Count} done";
        }
    }

    private ICommand? _hideSetupCommand;
    public ICommand HideSetupCommand => _hideSetupCommand ??= new RelayCommand(() =>
    {
        try
        {
            Store?.Settings.HideSetup(true);
        }
        catch (DataStoreException)
        {
            // Hidden for now; it may come back next time.
        }
        ShowSetup = false;
    });

    /// <summary>Works out which steps are done (when the dashboard is shown).</summary>
    public void RefreshSetup()
    {
        SetupSteps.Clear();
        if (Store is null || Access.IsStaff)
        {
            ShowSetup = false;
            return;
        }
        bool hidden;
        Core.Quotes.QuotationSettings company;
        AccountsSettings accounts;
        bool hasDefaultPricing, hasQuote;
        try
        {
            hidden = Store.Settings.IsSetupHidden();
            company = Store.Settings.LoadQuotationSettings();
            accounts = Store.Settings.LoadAccountsSettings();
            hasDefaultPricing = Store.Settings.HasDefaultPricing();
            hasQuote = Store.Projects.List().Count > 0;
        }
        catch (DataStoreException)
        {
            ShowSetup = false;
            return;
        }

        string name = Access.CompanyName.Length > 0 ? Access.CompanyName : company.CompanyName;
        bool hasGstin = Gst.IsGstin(company.Gstin);
        bool companyDone = name.Trim().Length > 0 && company.Address.Trim().Length > 0 && company.Phone.Trim().Length > 0
                           && (hasGstin || accounts.CompanyState.Length > 0);
        SetupSteps.Add(new SetupStep("Company details",
            companyDone ? "Address, phone and GSTIN are set: they print on quotations and invoices."
                : "Your address, phone and GSTIN (the GSTIN decides CGST + SGST or IGST on invoices).",
            companyDone, "Company & quotation", new RelayCommand(() => ShowView(AppView.QuotationSetup, AppArea.Sales))));

        bool pricesDone = Library.Profiles.Any(p => p.IsActive && p.CostPerMetre > 0);
        SetupSteps.Add(new SetupStep("Your prices",
            pricesDone ? "Profiles have prices." : "Your cost of profiles (per metre), glass (per m²) and hardware, in the Library Manager.",
            pricesDone, "Library Manager", new RelayCommand(OpenLibraryManager)));

        SetupSteps.Add(new SetupStep("Your pricing",
            hasDefaultPricing ? "New quotes start with your saved price structure."
                : "Your cost heads (wastage, labour, profit), discount and tax: set them on a quote's Pricing tab and Save as my default.",
            hasDefaultPricing, "Pricing", new RelayCommand(() => ShowView(AppView.Pricing))));

        SetupSteps.Add(new SetupStep("Your first quote",
            hasQuote ? "Quotes are saved." : "A quote: the client, then the designs, then the quotation PDF.",
            hasQuote, "New quote", new RelayCommand(NewQuote)));

        ShowSetup = !hidden && SetupSteps.Any(s => !s.IsDone);
        OnPropertyChanged(nameof(SetupProgressText));
    }
}
