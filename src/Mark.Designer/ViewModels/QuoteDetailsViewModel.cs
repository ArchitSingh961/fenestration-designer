using System.Text.Json;
using System.Windows.Input;
using Mark.Core.Models;

namespace Mark.Designer.ViewModels;

/// <summary>
/// The Client tab: project name, quote status, the client's name and contact, the site address and notes. The fields
/// are a form; Save (the header's, Ctrl+S or the form's own) first validates them and changes the model as one undoable
/// step, then stores the quote. The form reloads from the model
/// when the quote changes underneath it (another project opened, undo, redo), never while you are only typing.
/// </summary>
public sealed class QuoteDetailsViewModel : ViewModelBase
{
    private readonly Func<string, QuoteInfo, string?> _apply;
    private string _loadedState = "";

    public static IReadOnlyList<string> Titles { get; } = new[] { "", "Mr.", "Mrs.", "Ms.", "Dr.", "M/s." };

    public static IReadOnlyList<QuoteStatus> Statuses { get; } = Enum.GetValues<QuoteStatus>();

    /// <param name="apply">Stores the project name and quote details; returns an error message or null.</param>
    public QuoteDetailsViewModel(Func<string, QuoteInfo, string?> apply)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        ApplyCommand = new RelayCommand(Apply);
        RevertCommand = new RelayCommand(() => { if (_project is not null) Load(_project); });
    }

    private Project? _project;

    public ICommand ApplyCommand { get; }

    /// <summary>Throws away what was typed and shows the model's values again.</summary>
    public ICommand RevertCommand { get; }

    // ── Fields ──────────────────────────────────────────────────────

    private string _projectName = "";
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }

    private string _number = "";
    /// <summary>The quote number (read-only; given on first save).</summary>
    public string Number { get => _number; private set => SetProperty(ref _number, value); }

    private QuoteStatus _status;
    public QuoteStatus Status { get => _status; set => Set(ref _status, value); }

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _firstName = "";
    public string FirstName { get => _firstName; set => Set(ref _firstName, value); }

    private string _lastName = "";
    public string LastName { get => _lastName; set => Set(ref _lastName, value); }

    private string _company = "";
    public string Company { get => _company; set => Set(ref _company, value); }

    private string _phone = "";
    public string Phone { get => _phone; set => Set(ref _phone, value); }

    private string _email = "";
    public string Email { get => _email; set => Set(ref _email, value); }

    private string _address1 = "";
    public string AddressLine1 { get => _address1; set => Set(ref _address1, value); }

    private string _address2 = "";
    public string AddressLine2 { get => _address2; set => Set(ref _address2, value); }

    private string _city = "";
    public string City { get => _city; set => Set(ref _city, value); }

    private string _state = "";
    public string State { get => _state; set => Set(ref _state, value); }

    private string _postalCode = "";
    public string PostalCode { get => _postalCode; set => Set(ref _postalCode, value); }

    private string _gstin = "";
    /// <summary>The client's GSTIN (for a business client's tax invoice).</summary>
    public string Gstin { get => _gstin; set => Set(ref _gstin, value); }

    private string _country = "";
    public string Country { get => _country; set => Set(ref _country, value); }

    private string _notes = "";
    /// <summary>The client's requirements ("design needs"), site conditions, follow-ups.</summary>
    public string Notes { get => _notes; set => Set(ref _notes, value); }

    private bool _hasChanges;
    /// <summary>True while the form differs from the model (typed but not saved to the quote yet).</summary>
    public bool HasChanges { get => _hasChanges; private set => SetProperty(ref _hasChanges, value); }

    private string? _message;
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    private bool _messageIsError;
    public bool MessageIsError { get => _messageIsError; private set => SetProperty(ref _messageIsError, value); }

    // ── Model ↔ form ────────────────────────────────────────────────

    /// <summary>Shows <paramref name="project"/>'s details (discarding anything typed).</summary>
    public void Load(Project project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        var q = project.Quote;
        var c = q.Client;
        _projectName = project.Name;
        _status = q.Status;
        _title = c.Title;
        _firstName = c.FirstName;
        _lastName = c.LastName;
        _company = c.Company;
        _phone = c.Phone;
        _email = c.Email;
        _address1 = c.AddressLine1;
        _address2 = c.AddressLine2;
        _city = c.City;
        _state = c.State;
        _postalCode = c.PostalCode;
        _country = c.Country;
        _gstin = c.Gstin;
        _notes = q.Notes;
        Number = string.IsNullOrEmpty(q.Number) ? "New (numbered when saved)" : q.Number;
        _loadedState = StateOf(project.Name, q);
        HasChanges = false;
        Message = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Reloads if the model's quote changed since it was shown (undo, redo, save numbering), else keeps the form.</summary>
    public void SyncFromModel()
    {
        if (_project is null) return;
        string current = StateOf(_project.Name, _project.Quote);
        if (current == _loadedState)
        {
            Number = string.IsNullOrEmpty(_project.Quote.Number) ? "New (numbered when saved)" : _project.Quote.Number;
            return;
        }
        Load(_project);
    }

    /// <summary>The form's values as a quote (number untouched).</summary>
    public QuoteInfo ToQuote() => new()
    {
        Status = Status,
        Notes = Notes,
        Client = new ClientInfo
        {
            Title = Title, FirstName = FirstName, LastName = LastName, Company = Company, Phone = Phone, Email = Email,
            AddressLine1 = AddressLine1, AddressLine2 = AddressLine2, City = City, State = State, PostalCode = PostalCode,
            Country = Country, Gstin = Gstin
        }
    };

    private void Apply()
    {
        if (ApplyPending() is not null) return;
        Message = null;
    }

    /// <summary>
    /// Puts what was typed into the quote (one undoable step), as Save does before it stores the quote. Returns the
    /// problem (also shown on the form), or null when it was applied or nothing was typed.
    /// </summary>
    public string? ApplyPending()
    {
        if (!HasChanges) return null;
        string? error = _apply(ProjectName, ToQuote());
        if (error is not null)
        {
            Message = error;
            MessageIsError = true;
            return error;
        }
        if (_project is not null) Load(_project);
        return null;
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name))
            HasChanges = true;
    }

    private static string StateOf(string name, QuoteInfo quote)
        => name + "\u0001" + JsonSerializer.Serialize(new { quote.Status, quote.Notes, quote.Client });
}
