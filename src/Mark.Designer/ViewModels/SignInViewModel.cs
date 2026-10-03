using System.Windows.Input;
using Mark.Licensing.Client;

namespace Mark.Designer.ViewModels;

/// <summary>
/// The sign-in page of MARK: User ID and password given by the MARK supplier, "Keep me signed in", and the licence
/// server address (under "Connection"). <see cref="SignedIn"/> is raised once the licence is in place.
/// </summary>
public sealed class SignInViewModel : ViewModelBase
{
    private readonly LicenceManager _manager;

    public SignInViewModel(LicenceManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _serverUrl = manager.ServerUrl.TrimEnd('/');
        _userId = manager.RememberedUserId ?? "";
        _keepSignedIn = manager.KeepSignedIn;
        SignInCommand = new AsyncCommand(SignInAsync);
    }

    public event Action? SignedIn;

    private string _serverUrl;
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    private string _userId;
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    /// <summary>Set by the view (a PasswordBox cannot be bound).</summary>
    public string Password { get; set; } = "";

    private bool _keepSignedIn;
    public bool KeepSignedIn
    {
        get => _keepSignedIn;
        set => SetProperty(ref _keepSignedIn, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    /// <summary>"This computer: DESKTOP-1234" (what the owner sees in the list of computers).</summary>
    public string ComputerText => $"This computer: {_manager.MachineName}";

    public ICommand SignInCommand { get; }

    public async Task SignInAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            Error = await _manager.SignInAsync(ServerUrl, UserId, Password, KeepSignedIn);
        }
        finally
        {
            IsBusy = false;
        }
        if (Error is null)
            SignedIn?.Invoke();
    }
}
