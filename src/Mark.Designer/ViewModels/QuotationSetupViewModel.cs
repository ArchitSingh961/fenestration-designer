using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Mark.Core.Quotes;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Sales › Quotation setup: what the company prints on every quotation — its address, contact and GSTIN, an optional
/// partner line and brand (with logo), the covering letter, terms and conditions, bank details, the acceptance line,
/// square feet or square metres, the money label, and an optional last page with a picture. The company name and logo
/// come from the MARK account.
/// </summary>
public sealed class QuotationSetupViewModel : ViewModelBase
{
    private const int MaxImageBytes = 2 * 1024 * 1024;
    private readonly Func<SettingsRepository?> _settings;
    private readonly Func<IDialogService?> _dialogs;
    private readonly Func<Mark.Licensing.Api.QuotationProfile?> _profile;
    private QuotationSettings _loaded = new();

    /// <param name="profile">With a MARK account: the details, brand, bank and last page the MARK supplier set (not shown
    /// on the page, but printed); null: no account, the company fills them in itself.</param>
    public QuotationSetupViewModel(Func<SettingsRepository?> settings, Func<IDialogService?> dialogs,
        Func<Mark.Licensing.Api.QuotationProfile?>? profile = null)
    {
        _settings = settings;
        _dialogs = dialogs;
        _profile = profile ?? (() => null);
        SaveCommand = new RelayCommand(Save);
        RevertCommand = new RelayCommand(Load);
        ChooseBrandLogoCommand = new RelayCommand(() => BrandLogo = ChooseImage("Brand logo") ?? BrandLogo);
        RemoveBrandLogoCommand = new RelayCommand(() => BrandLogo = null);
        ChooseExtraPageCommand = new RelayCommand(() => ExtraPage = ChooseImage("Last page picture") ?? ExtraPage);
        RemoveExtraPageCommand = new RelayCommand(() => ExtraPage = null);
        DefaultTextsCommand = new RelayCommand(() =>
        {
            Letter = QuotationSettings.DefaultLetter;
            Terms = QuotationSettings.DefaultTerms;
            Acceptance = QuotationSettings.DefaultAcceptance;
        });
    }

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand ChooseBrandLogoCommand { get; }
    public ICommand RemoveBrandLogoCommand { get; }
    public ICommand ChooseExtraPageCommand { get; }
    public ICommand RemoveExtraPageCommand { get; }
    public ICommand DefaultTextsCommand { get; }

    private string _companyName = "", _partnerLabel = "", _address = "", _phone = "", _email = "", _website = "", _gstin = "",
        _brandName = "", _letter = "", _terms = "", _bankAccountName = "", _bankAccountNumber = "", _bankName = "", _bankIfsc = "",
        _bankBranch = "", _acceptance = "", _notes = "", _currencyLabel = "";

    public string CompanyName { get => _companyName; set => SetProperty(ref _companyName, value); }
    public string PartnerLabel { get => _partnerLabel; set => SetProperty(ref _partnerLabel, value); }
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public string Phone { get => _phone; set => SetProperty(ref _phone, value); }
    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string Website { get => _website; set => SetProperty(ref _website, value); }
    public string Gstin { get => _gstin; set => SetProperty(ref _gstin, value); }
    public string BrandName { get => _brandName; set => SetProperty(ref _brandName, value); }
    public string Letter { get => _letter; set => SetProperty(ref _letter, value); }
    public string Terms { get => _terms; set => SetProperty(ref _terms, value); }
    public string BankAccountName { get => _bankAccountName; set => SetProperty(ref _bankAccountName, value); }
    public string BankAccountNumber { get => _bankAccountNumber; set => SetProperty(ref _bankAccountNumber, value); }
    public string BankName { get => _bankName; set => SetProperty(ref _bankName, value); }
    public string BankIfsc { get => _bankIfsc; set => SetProperty(ref _bankIfsc, value); }
    public string BankBranch { get => _bankBranch; set => SetProperty(ref _bankBranch, value); }
    public string Acceptance { get => _acceptance; set => SetProperty(ref _acceptance, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string CurrencyLabel { get => _currencyLabel; set => SetProperty(ref _currencyLabel, value); }

    private AreaUnit _areaUnit;
    public AreaUnit AreaUnit
    {
        get => _areaUnit;
        set
        {
            if (!SetProperty(ref _areaUnit, value)) return;
            OnPropertyChanged(nameof(IsSquareFeet));
            OnPropertyChanged(nameof(IsSquareMetres));
        }
    }

    public bool IsSquareFeet { get => _areaUnit == AreaUnit.SquareFeet; set { if (value) AreaUnit = AreaUnit.SquareFeet; } }
    public bool IsSquareMetres { get => _areaUnit == AreaUnit.SquareMetres; set { if (value) AreaUnit = AreaUnit.SquareMetres; } }

    private string? _brandLogo;
    /// <summary>The brand logo, base64 (null: none).</summary>
    public string? BrandLogo
    {
        get => _brandLogo;
        set
        {
            if (!SetProperty(ref _brandLogo, value)) return;
            OnPropertyChanged(nameof(BrandLogoImage));
            OnPropertyChanged(nameof(HasBrandLogo));
        }
    }

    public ImageSource? BrandLogoImage => Decode(_brandLogo);
    public bool HasBrandLogo => _brandLogo is not null;

    private string? _extraPage;
    /// <summary>The last page's picture, base64 (null: none).</summary>
    public string? ExtraPage
    {
        get => _extraPage;
        set
        {
            if (!SetProperty(ref _extraPage, value)) return;
            OnPropertyChanged(nameof(ExtraPageImage));
            OnPropertyChanged(nameof(HasExtraPage));
        }
    }

    public ImageSource? ExtraPageImage => Decode(_extraPage);
    public bool HasExtraPage => _extraPage is not null;

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    private bool _isManaged;
    /// <summary>Details, brand, bank details and last page come from the MARK supplier: the page leaves them out.</summary>
    public bool IsManaged
    {
        get => _isManaged;
        private set
        {
            if (SetProperty(ref _isManaged, value)) OnPropertyChanged(nameof(CanEditDetails));
        }
    }

    public bool CanEditDetails => !_isManaged;

    /// <summary>Reads the saved setup into the form.</summary>
    public void Load()
    {
        try
        {
            _loaded = _settings()?.LoadQuotationSettings() ?? new QuotationSettings();
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
            _loaded = new QuotationSettings();
        }
        var s = _loaded;
        CompanyName = s.CompanyName; PartnerLabel = s.PartnerLabel; Address = s.Address; Phone = s.Phone; Email = s.Email; Website = s.Website;
        Gstin = s.Gstin; BrandName = s.BrandName; BrandLogo = s.BrandLogoBase64; Letter = s.Letter; Terms = s.Terms;
        BankAccountName = s.BankAccountName; BankAccountNumber = s.BankAccountNumber; BankName = s.BankName; BankIfsc = s.BankIfsc;
        BankBranch = s.BankBranch; Acceptance = s.Acceptance; Notes = s.Notes; AreaUnit = s.AreaUnit; CurrencyLabel = s.CurrencyLabel;
        ExtraPage = s.ExtraPageBase64;

        var managed = _profile();
        IsManaged = managed is not null;
        if (managed is { } p)
        {
            PartnerLabel = p.PartnerLabel; Address = p.Address; Phone = p.Phone; Email = p.Email; Website = p.Website; Gstin = p.Gstin;
            BrandName = p.BrandName; BrandLogo = p.BrandLogoBase64; BankAccountName = p.BankAccountName; BankAccountNumber = p.BankAccountNumber;
            BankName = p.BankName; BankIfsc = p.BankIfsc; BankBranch = p.BankBranch; ExtraPage = p.ExtraPageBase64;
        }
    }

    /// <summary>The setup as entered (with an account, the supplier's details stay as they were saved locally).</summary>
    public QuotationSettings ToSettings() => IsManaged
        ? _loaded with
        {
            CompanyName = CompanyName.Trim(), Letter = Letter.Trim(), Terms = Terms.Trim(), Acceptance = Acceptance.Trim(), Notes = Notes.Trim(),
            AreaUnit = AreaUnit, CurrencyLabel = string.IsNullOrWhiteSpace(CurrencyLabel) ? "Rs." : CurrencyLabel.Trim()
        }
        : new()
    {
        CompanyName = CompanyName.Trim(), PartnerLabel = PartnerLabel.Trim(), Address = Address.Trim(), Phone = Phone.Trim(),
        Email = Email.Trim(), Website = Website.Trim(), Gstin = Gstin.Trim().ToUpperInvariant(), BrandName = BrandName.Trim(),
        BrandLogoBase64 = BrandLogo, Letter = Letter.Trim(), Terms = Terms.Trim(), BankAccountName = BankAccountName.Trim(),
        BankAccountNumber = BankAccountNumber.Trim(), BankName = BankName.Trim(), BankIfsc = BankIfsc.Trim().ToUpperInvariant(),
        BankBranch = BankBranch.Trim(), Acceptance = Acceptance.Trim(), Notes = Notes.Trim(), AreaUnit = AreaUnit,
        CurrencyLabel = string.IsNullOrWhiteSpace(CurrencyLabel) ? "Rs." : CurrencyLabel.Trim(), ExtraPageBase64 = ExtraPage
    };

    public void Save()
    {
        if (_settings() is not { } settings)
        {
            Show("There is no local database, so the setup cannot be saved.", true);
            return;
        }
        try
        {
            settings.SaveQuotationSettings(ToSettings());
            _loaded = ToSettings();
            Show("Saved. Every quotation PDF from now on uses this setup.", false);
        }
        catch (DataStoreException ex)
        {
            Show(ex.Message, true);
        }
    }

    private string? ChooseImage(string title)
    {
        if (_dialogs()?.ChooseOpenFile(title, "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg") is not { } path) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaxImageBytes)
            {
                Show($"The picture is too large ({bytes.Length / 1024} KB). Use one of at most {MaxImageBytes / 1024 / 1024} MB.", true);
                return null;
            }
            string base64 = Convert.ToBase64String(bytes);
            if (Decode(base64) is null)
            {
                Show("The file is not a picture MARK can use. Use a PNG or JPEG.", true);
                return null;
            }
            Message = null;
            return base64;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show($"The picture could not be read: {ex.Message}", true);
            return null;
        }
    }

    private static ImageSource? Decode(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(Convert.FromBase64String(base64));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException or InvalidOperationException
                                       or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private void Show(string message, bool isError)
    {
        MessageIsError = isError;
        Message = message;
    }
}
