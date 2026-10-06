using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
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
        if (Store is not null)
        {
            Store.Projects.User = CurrentUser;
            Store.Enquiries.User = CurrentUser;
            Store.Production.User = CurrentUser;
        }
        OnPropertyChanged(nameof(HasStaff));
        OnPropertyChanged(nameof(QuoteTotalText));
        Cutting.ShowsCosts = Access.CanSeeQuoteValues;
        RefreshCompanyDesigns();
        OnViewChanged();
        RefreshNavigation();
    }

    /// <summary>Who is working: the signed-in login, or the Windows user without a licence.</summary>
    public Mark.Data.ProjectUser CurrentUser => Access.IsLicensed
        ? new Mark.Data.ProjectUser(Access.UserId, Access.UserName)
        : new Mark.Data.ProjectUser(Environment.UserName, Environment.UserName);

    private StaffViewModel? _staff;

    /// <summary>The Staff page (set by the app when the account owner is signed in).</summary>
    public StaffViewModel? Staff
    {
        get => _staff;
        set
        {
            if (SetProperty(ref _staff, value))
            {
                OnPropertyChanged(nameof(HasStaff));
                RefreshNavigation();
            }
        }
    }

    /// <summary>The account owner can open the Staff page.</summary>
    public bool HasStaff => _staff is not null && Access.CanManageStaff;

    /// <summary>
    /// The company's own systems (from the owner's catalogue) as a category of the design library, named after the
    /// company. Without own systems the category is not shown.
    /// </summary>
    public void RefreshCompanyDesigns()
    {
        Mark.Core.Library.OwnItemsLabel? own = null;
        try
        {
            if (Store is not null && Access.IsCatalogueManaged) own = Store.Settings.LoadOwnItems();
        }
        catch (Mark.Data.DataStoreException)
        {
            // Without the label there is no company category.
        }
        var designs = own is null ? new List<SystemDesigns>()
            : Library.Systems.Where(x => x.IsActive && own.Contains(x.Id))
                .Select(x => new SystemDesigns(x, DesignTemplates.ForSystem(x, Library))).ToList();
        DesignLibrary.SetCompanyDesigns(own?.CompanyName, designs);
    }

    // ── Product systems (Milestone 13) ─────────────────────────────

    /// <summary>The frame a profile or glass panel belongs to, or null.</summary>
    private Frame? FrameOf(Guid objectId)
        => Project.Frames.FirstOrDefault(f => f.Profiles.Any(p => p.Id == objectId) || f.GlassPanels.Any(g => g.Id == objectId));

    /// <summary>
    /// Puts the selected frames (or the frames of the selected objects) in a product system, one undo step. Returns an
    /// error message, or null.
    /// </summary>
    public string? AssignSystem(string? systemId)
    {
        if (IsOutsideView) return "Switch to the Inside view to change the design.";
        var frames = Project.Frames.Where(f => Selection.Contains(f.Id)).ToList();
        if (frames.Count == 0 && SingleSelectedFrame is { } single) frames.Add(single);
        if (frames.Count == 0) return "Select a frame first.";
        var commands = frames.Select(f => (IUndoableCommand)new SetFrameSystemCommand(f, systemId, Library, Rules))
            .ToList();
        return RunForMessage(() => CompositeCommand.Combine(commands[0].Description, commands)!);
    }

    /// <summary>The New design panel: reference, quantity, location, brand and glass of a design just made.</summary>
    public NewDesignViewModel NewDesignPanel => _newDesignPanel ??= new NewDesignViewModel(() => Library, SetUpNewDesign)
    {
        StartNext = StartNextDesign,
        DiscardStarted = DiscardStartedDesign
    };

    // The undo count just after a design was started by itself: if nothing else happened, Cancel undoes it cleanly.
    private int _startedNextUndoCount = -1;

    /// <summary>After a design is applied: a new one of the same size, to the right, with the next ref., asked at once.</summary>
    private void StartNextDesign(double width, double height)
    {
        if (IsOutsideView || !Access.CanUseDrawing || Access.ReadOnlyMessage is not null) return;
        double x = Project.Frames.Count == 0 ? 0 : Project.Frames.Max(f => f.X + f.Width) + Rules.FrameSpacingMm;
        if (Run(() => Core.Commands.CreateFrameCommand.Create(Project, x, 0, width, height, Rules)) is not { } command) return;
        _startedNextUndoCount = CommandHistory.UndoCount;
        Select(command.Frame.Id);
        Canvas.FitToContent();
        NewDesignPanel.Open(command.Frame, startedNext: true);
        ShowNotice($"{NewDesignPanel.Reference}: the next design. Done (or Esc) when there are no more.");
    }

    /// <summary>A design started by itself was cancelled: it goes again (undone if nothing else happened since).</summary>
    private void DiscardStartedDesign(Guid frameId)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return;
        if (CommandHistory.UndoCount == _startedNextUndoCount) Undo();
        else RunForMessage(() => new DeleteFrameCommand(Project, frame));
        _startedNextUndoCount = -1;
        Notice = null;
        Canvas.FitToContent();
    }

    private NewDesignViewModel? _newDesignPanel;

    /// <summary>Asks for the details of a new frame (the brand only when <paramref name="askBrand"/>).</summary>
    public void AskNewDesign(Frame frame, bool askBrand = true)
    {
        if (IsOutsideView || !Access.CanUseDrawing) return;
        NewDesignPanel.Open(frame, askBrand);
    }

    /// <summary>Sets up one frame from the New design panel, one undo step. Returns an error, or null.</summary>
    public string? SetUpNewDesign(Guid frameId, NewDesignChoice choice)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return "The design is no longer there.";
        var info = frame.Design.Copy();
        info.Reference = choice.Reference;
        info.Quantity = choice.Quantity;
        info.Location = choice.Location;
        // Size, type and details in one undo step.
        var steps = new List<IUndoableCommand>();
        if (choice.WidthMm is { } width && choice.HeightMm is { } height)
            steps.Add(new ResizeFrameCommand(frame, width, height, Rules));
        if (choice.TemplateId is { } templateId && Core.Design.DesignTemplates.Find(templateId) is { } template)
            steps.Add(new ApplyTemplateCommand(frame, template, null, Rules));
        steps.Add(new SetUpNewDesignCommand(frame, choice.SystemId, choice.GlassId, info, Library, Rules));
        string? error = RunForMessage(() => CompositeCommand.Combine("New design", steps)!);
        if (error is null)
        {
            Select(frame.Id);
            if (steps.Count > 1) Canvas.FitToContent();                       // the new size is in view
        }
        return error;
    }

    /// <summary>Why a design cannot be applied under the company's package, or null.</summary>
    private string? DesignBlocked(DesignTemplate template)
    {
        if (!Access.CanUseDesignLibrary) return Access.DesignLibraryLock;
        if (!template.IsDividerOnly && !Access.CanUseOpenings) return Access.OpeningsLock;
        return null;
    }
}
