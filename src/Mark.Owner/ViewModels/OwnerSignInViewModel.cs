using System.Windows.Input;
using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>
/// MARK Owner's sign-in page. It first asks the licence server whether an admin exists: if not (a new server, on this
/// computer) it offers to create the admin account; otherwise the admin signs in. <see cref="SignedIn"/> gives the
/// signed-in client.
/// </summary>
public sealed class OwnerSignInViewModel : ViewModelBase
{
    private readonly Func<string, OwnerApiClient> _clientFor;
    private OwnerApiClient? _client;

    public OwnerSignInViewModel(Func<string, OwnerApiClient> clientFor, string serverUrl, string? userId)
    {
        _clientFor = clientFor ?? throw new ArgumentNullException(nameof(clientFor));
        _serverUrl = serverUrl;
        _userId = userId ?? "";
        ConnectCommand = new AsyncCommand(ConnectAsync);
        SignInCommand = new AsyncCommand(SignInAsync);
    }

    public event Action<OwnerApiClient>? SignedIn;

    public ICommand ConnectCommand { get; }
    public ICommand SignInCommand { get; }

    private string _serverUrl;
    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (SetProperty(ref _serverUrl, value))
                IsConnected = false;
        }
    }

    private string _userId;
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public string Password { get; set; } = "";

    public string ConfirmPassword { get; set; } = "";

    private string _name = "";
    /// <summary>The admin's name (setting up only).</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private bool _isSetUp;
    /// <summary>True when the server has no admin yet and this computer may create it.</summary>
    public bool IsSetUp
    {
        get => _isSetUp;
        private set
        {
            if (!SetProperty(ref _isSetUp, value)) return;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string Title => IsSetUp ? "Set up the admin account" : "Sign in";

    public string ButtonText => IsSetUp ? "Create admin account" : "Sign in";

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

    private string? _warning;
    /// <summary>The server's signing key does not match MARK: licences it issues would be rejected.</summary>
    public string? Warning
    {
        get => _warning;
        private set
        {
            if (SetProperty(ref _warning, value))
                OnPropertyChanged(nameof(HasWarning));
        }
    }

    public bool HasWarning => _warning is not null;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Asks the server whether it has an admin. Returns false (with <see cref="Error"/>) when it cannot be reached.</summary>
    public async Task<bool> ConnectAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            _client = _clientFor(ServerUrl);
            var status = await _client.StatusAsync();
            IsSetUp = !status.HasAdmin;
            Warning = status.PublicKeyMatchesMark ? null
                : "This licence server's signing key is not the one MARK is built with: MARK will not accept the licences it issues.";
            if (!status.HasAdmin && !status.CanSetUpHere)
            {
                Error = "This licence server has no admin account yet. Set it up with MARK Owner on the server's own computer.";
                return false;
            }
            IsConnected = true;
            return true;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return false;
        }
        catch (LicenceServerException ex)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SignInAsync()
    {
        if (!IsConnected && !await ConnectAsync()) return;
        Error = null;
        if (IsSetUp)
        {
            if (string.IsNullOrWhiteSpace(Name)) { Error = "Enter your name."; return; }
            if (PasswordHasher.Check(Password) is { } weak) { Error = weak; return; }
            if (Password != ConfirmPassword) { Error = "The two passwords are not the same."; return; }
        }
        else if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrEmpty(Password))
        {
            Error = "Enter your User ID and password.";
            return;
        }

        IsBusy = true;
        try
        {
            if (IsSetUp) await _client!.SetUpAsync(new AdminSetupRequest(UserId.Trim(), Password, Name.Trim()));
            else await _client!.SignInAsync(new AdminSignInRequest(UserId.Trim(), Password));
        }
        catch (LicenceServerException ex)
        {
            Error = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }
        SignedIn?.Invoke(_client!);
    }
}
