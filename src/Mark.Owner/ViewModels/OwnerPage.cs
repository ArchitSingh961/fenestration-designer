using Mark.Designer.ViewModels;
using Mark.Licensing;
using Mark.Licensing.Api;

namespace Mark.Owner.ViewModels;

/// <summary>The dialogs MARK Owner's view models need (the WPF shell implements it; tests pass a fake).</summary>
public interface IOwnerDialogs
{
    bool Confirm(string title, string message);

    /// <summary>A PNG or JPEG file for a company logo, or null if cancelled.</summary>
    string? ChooseImageFile();

    void CopyText(string text);
}

/// <summary>
/// Base of MARK Owner's pages: a message line and <see cref="RunAsync"/>, which reports server errors as messages and
/// ends the session when the server says it has expired.
/// </summary>
public abstract class OwnerPage : ViewModelBase
{
    protected OwnerPage(OwnerApiClient api, IOwnerDialogs dialogs, Action sessionEnded)
    {
        Api = api ?? throw new ArgumentNullException(nameof(api));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SessionEnded = sessionEnded ?? throw new ArgumentNullException(nameof(sessionEnded));
    }

    protected OwnerApiClient Api { get; }

    protected IOwnerDialogs Dialogs { get; }

    private Action SessionEnded { get; }

    private string? _message;
    public string? Message
    {
        get => _message;
        protected set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => _message is not null;

    private bool _messageIsError;
    public bool MessageIsError
    {
        get => _messageIsError;
        protected set => SetProperty(ref _messageIsError, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    protected void Show(string? message, bool isError = false)
    {
        MessageIsError = isError;
        Message = message;
    }

    /// <summary>Runs a server call; returns false (with the error shown) when it failed.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null) Show(success);
            return true;
        }
        catch (LicenceServerException ex) when (ex.Code == ErrorCodes.Unauthorized)
        {
            Show(ex.Message, true);
            SessionEnded();
            return false;
        }
        catch (LicenceServerException ex)
        {
            Show(ex.Message, true);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads the page's data from the server.</summary>
    public abstract Task LoadAsync();
}

/// <summary>A choice of how long something is valid, e.g. "1 year".</summary>
public sealed record ValidityChoice(string Name, int Months, int Days = 0)
{
    public static IReadOnlyList<ValidityChoice> All { get; } = new[]
    {
        new ValidityChoice("14 days", 0, 14),
        new ValidityChoice("1 month", 1),
        new ValidityChoice("3 months", 3),
        new ValidityChoice("6 months", 6),
        new ValidityChoice("1 year", 12),
        new ValidityChoice("2 years", 24)
    };

    /// <summary>The local date this choice ends on, counted from <paramref name="from"/>.</summary>
    public DateTime From(DateTime from) => from.Date.AddMonths(Months).AddDays(Days);

    public override string ToString() => Name;
}

/// <summary>Helpers for showing licence data in MARK Owner.</summary>
public static class OwnerText
{
    /// <summary>"Active", "Suspended", "Expired" or "Ends in 9 days".</summary>
    public static string StateOf(bool suspended, DateTime validUntilUtc, DateTime nowUtc)
    {
        if (suspended) return "Suspended";
        if (validUntilUtc < nowUtc) return "Expired";
        int days = (int)Math.Ceiling((validUntilUtc - nowUtc).TotalDays);
        return days <= LicenceEvaluator.WarnDays ? $"Ends in {days} day{(days == 1 ? "" : "s")}" : "Active";
    }

    /// <summary>"uPVC until 3 Oct 2027 · Aluminium until 1 Jan 2027".</summary>
    public static string ProductsText(IEnumerable<ProductLicence> products)
        => string.Join(" · ", products.Select(p => $"{p.Product.DisplayName()}{(p.Suspended ? " (suspended)" : "")} until {LicenceDates.Format(p.ValidUntilUtc)}"));

    public static string When(DateTime? utc) => utc is { } value ? value.ToLocalTime().ToString("d MMM yyyy, HH:mm") : "never";
}
