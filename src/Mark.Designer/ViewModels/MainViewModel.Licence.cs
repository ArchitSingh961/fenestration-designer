using Mark.Core.Design;
using Mark.Licensing;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 12: what the signed-in company may use. Features outside its package are locked (with a message saying
/// so) and a read-only licence blocks saving; the Account page shows the licence. Without a licence everything works
/// (see <see cref="AccessViewModel"/>).
/// </summary>
public partial class MainViewModel
{
    /// <summary>The company's access: features, read-only state, name and logo.</summary>
    public AccessViewModel Access { get; } = new();

    private AccountViewModel? _account;

    /// <summary>The Account page (set by the app when MARK runs with sign-in).</summary>
    public AccountViewModel? Account
    {
        get => _account;
        set
        {
            if (SetProperty(ref _account, value))
                OnPropertyChanged(nameof(HasAccount));
        }
    }

    public bool HasAccount => _account is not null;

    private void CreateLicenceFeatures()
    {
        Access.Changed += OnAccessChanged;
        Quotes.Blocked = () => Access.ReadOnlyMessage;
    }

    private void OnAccessChanged()
    {
        RaisePersistenceCanExecute();
        Canvas.InvalidateContent();
    }

    /// <summary>Why a design cannot be applied under the company's package, or null.</summary>
    private string? DesignBlocked(DesignTemplate template)
    {
        if (!Access.CanUseDesignLibrary) return Access.DesignLibraryLock;
        if (!template.IsDividerOnly && !Access.CanUseOpenings) return Access.OpeningsLock;
        return null;
    }
}
