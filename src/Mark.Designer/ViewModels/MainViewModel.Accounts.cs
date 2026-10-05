using Mark.Core.Accounts;
using Mark.Core.Quotes;
using Mark.Data;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>Milestone 19, accounts: Accounts › Invoices, Receipts, Outstanding, and Export and setup.</summary>
public partial class MainViewModel
{
    public AccountsViewModel Accounts { get; private set; } = null!;

    private void CreateAccountsFeatures()
    {
        Accounts = new AccountsViewModel(() => Store, () => Library, () => Calculation.Rules, () => Dialogs, CompanyPaper, () => Access.UserName)
        {
            Blocked = () => Store is null ? "There is no local database."
                : Access.ReadOnlyMessage ?? (Access.Allows(Features.Invoices) ? null : Access.Lock(Features.Invoices))
        };
    }

    /// <summary>The company as on its quotations (Quotation setup and the supplier's profile): name, address, GSTIN, bank.</summary>
    private CompanyPaper CompanyPaper()
    {
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
        string? state = Gst.StateCodeOfGstin(settings.Gstin);
        var lines = QuotationSettings.LinesOf(settings.Address).Concat(new[]
        {
            settings.Phone.Length > 0 ? $"Phone: {settings.Phone}" : "",
            settings.Email.Length > 0 ? $"Email: {settings.Email}" : "",
            settings.Gstin.Length > 0 ? $"GSTIN: {settings.Gstin}" : "",
            state is not null ? $"State: {Gst.StateText(state)}" : ""
        }).Where(l => l.Length > 0).ToList();
        var bank = new[]
        {
            settings.BankAccountName.Length > 0 ? $"Account name: {settings.BankAccountName}" : "",
            settings.BankAccountNumber.Length > 0 ? $"Account number: {settings.BankAccountNumber}" : "",
            settings.BankName.Length > 0 ? $"Bank: {settings.BankName}{(settings.BankBranch.Length > 0 ? ", " + settings.BankBranch : "")}" : "",
            settings.BankIfsc.Length > 0 ? $"IFSC: {settings.BankIfsc}" : ""
        }.Where(l => l.Length > 0).ToList();
        return new CompanyPaper(company, lines, settings.Gstin, Access.Logo is null ? null : LogoBytes(), bank);
    }

    private byte[]? LogoBytes()
    {
        try
        {
            return Access.Status?.Licence.LogoBase64 is { Length: > 0 } base64 ? Convert.FromBase64String(base64) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
