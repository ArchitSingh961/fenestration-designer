using System.Windows.Input;
using Fenestration.Core.Commands;
using Fenestration.Core.Design;
using Fenestration.Core.Interfaces;
using Fenestration.Core.Models;
using Fenestration.Core.Serialization;
using Fenestration.Core.Viewport;
using Fenestration.Designer.Interaction;
using Fenestration.Designer.Rendering;
using Fenestration.Designer.Tools;

namespace Fenestration.Designer.ViewModels;

/// <summary>
/// Top-level view model. Owns the current project, selection state, command history,
/// canvas view model and the frame-designer actions.
///
/// Data flow for every edit: user action → undoable command → <see cref="FrameEditor"/> (validates,
/// updates the domain model, re-derives glass) → <see cref="OnDesignChanged"/> → the renderer redraws
/// from the model.
/// </summary>
public class MainViewModel : ViewModelBase, IDesignService
{
    /// <summary>
    /// Fit to Screen leaves this fraction of the viewport as margin: room for the dimensions,
    /// which are drawn a fixed pixel distance outside each frame.
    /// </summary>
    private const double FitMarginForDimensions = 0.25;

    public MainViewModel()
    {
        Rules = new DesignRules();
        Rules.Validate();
        Canvas = new CanvasViewModel(new ViewportSettings { FitMarginFraction = FitMarginForDimensions });
        Properties = new PropertiesViewModel
        {
            ResizeFrame = ResizeSelectedFrame,
            MoveDivision = MoveSelectedDivision
        };
        CommandHistory = new CommandHistory();
        ActiveTool = new SelectTool(this);

        NewProjectCommand = new RelayCommand(NewProject);
        ResetViewCommand = Canvas.ResetViewCommand;
        UndoCommand = new RelayCommand(() => CommandHistory.Undo(), () => CommandHistory.CanUndo);
        RedoCommand = new RelayCommand(() => CommandHistory.Redo(), () => CommandHistory.CanRedo);
        CreateFrameCommand = new RelayCommand(CreateFrame);
        AddMullionCommand = new RelayCommand(() => AddDivision(MemberAxis.Vertical));
        AddTransomCommand = new RelayCommand(() => AddDivision(MemberAxis.Horizontal));
        DeleteSelectedCommand = new RelayCommand(DeleteSelection, () => HasSelection);

        CommandHistory.HistoryChanged += () =>
        {
            ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
            OnDesignChanged();
        };

        // Only properties shown in StatusText; cursor moves must not rebuild the status string.
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasViewModel.SnapToGrid))
                OnPropertyChanged(nameof(StatusText));
        };

        Canvas.ContentLayers.Add(new ProjectLayer(() => Project, IsSelected));

        NewProject();
    }

    // ── Sub view models & services ──────────────────────────────────

    public CanvasViewModel Canvas { get; }
    public PropertiesViewModel Properties { get; }
    public CommandHistory CommandHistory { get; }
    public DesignRules Rules { get; }

    /// <summary>The tool receiving left-button input in the viewport.</summary>
    public IViewportTool ActiveTool { get; }

    // ── Project ─────────────────────────────────────────────────────

    private Project _project = new();
    public Project Project
    {
        get => _project;
        private set
        {
            if (SetProperty(ref _project, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private string? _projectFilePath;
    public string? ProjectFilePath
    {
        get => _projectFilePath;
        set
        {
            if (SetProperty(ref _projectFilePath, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public string Title => _projectFilePath != null
        ? $"eVA Fenestration Designer — {System.IO.Path.GetFileName(_projectFilePath)}"
        : $"eVA Fenestration Designer — {Project.Name}";

    /// <summary>
    /// Call after anything changes the design outside the command history (e.g. a live drag):
    /// redraws the content and refreshes the properties panel and status bar from the model.
    /// </summary>
    public void OnDesignChanged()
    {
        Canvas.InvalidateContent();
        RefreshProperties();
        OnPropertyChanged(nameof(StatusText));
    }

    // ── Selection ───────────────────────────────────────────────────

    private readonly HashSet<Guid> _selectedIds = new();

    public IReadOnlySet<Guid> SelectedIds => _selectedIds;

    public bool HasSelection => _selectedIds.Count > 0;

    public void Select(Guid id, bool addToSelection = false)
    {
        if (!addToSelection)
            _selectedIds.Clear();
        _selectedIds.Add(id);
        OnSelectionChanged();
    }

    public void Deselect(Guid id)
    {
        _selectedIds.Remove(id);
        OnSelectionChanged();
    }

    public void ClearSelection()
    {
        _selectedIds.Clear();
        OnSelectionChanged();
    }

    public bool IsSelected(Guid id) => _selectedIds.Contains(id);

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIds));
        ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        RefreshProperties();
        Canvas.InvalidateContent();
    }

    /// <summary>Rebuilds the properties panel from the current selection.</summary>
    private void RefreshProperties()
    {
        // Drop ids whose objects no longer exist (e.g. after undo of a create).
        if (_selectedIds.RemoveWhere(id => FindObject(id) is null) > 0)
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();

        if (_selectedIds.Count == 0)
        {
            Properties.ShowNothing();
            return;
        }

        if (_selectedIds.Count > 1)
        {
            Properties.ShowMultiple(_selectedIds.Count);
            return;
        }

        switch (FindObject(_selectedIds.First()))
        {
            case Frame frame: Properties.ShowFrame(frame); break;
            case Profile profile: Properties.ShowProfile(profile); break;
            case GlassPanel glass: Properties.ShowGlass(glass); break;
            default: Properties.ShowNothing(); break;
        }
    }

    /// <summary>Finds a frame, profile, glass panel or dimension by Id.</summary>
    public object? FindObject(Guid id)
    {
        foreach (var frame in Project.Frames)
        {
            if (frame.Id == id) return frame;
            object? child = (object?)frame.Profiles.FirstOrDefault(p => p.Id == id)
                ?? (object?)frame.GlassPanels.FirstOrDefault(g => g.Id == id)
                ?? frame.Dimensions.FirstOrDefault(d => d.Id == id);
            if (child is not null) return child;
        }
        return null;
    }

    /// <summary>The frame that is, or contains, the object with this Id.</summary>
    public Frame? FindFrameOf(Guid id)
        => Project.Frames.FirstOrDefault(f => f.Id == id
            || f.Profiles.Any(p => p.Id == id)
            || f.GlassPanels.Any(g => g.Id == id));

    // ── Commands ────────────────────────────────────────────────────

    public ICommand NewProjectCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ResetViewCommand { get; }
    public ICommand CreateFrameCommand { get; }
    public ICommand AddMullionCommand { get; }
    public ICommand AddTransomCommand { get; }
    public ICommand DeleteSelectedCommand { get; }

    private void NewProject()
    {
        Project = new Project { Name = "New Project" };
        CommandHistory.Clear();
        ClearSelection();
        ProjectFilePath = null;
        DesignMessage = null;
        Canvas.InvalidateContent();
        Canvas.FitToContent();
    }

    // ── Frame designer ──────────────────────────────────────────────

    private string _newFrameWidthText = "1200";
    public string NewFrameWidthText
    {
        get => _newFrameWidthText;
        set => SetProperty(ref _newFrameWidthText, value);
    }

    private string _newFrameHeightText = "1500";
    public string NewFrameHeightText
    {
        get => _newFrameHeightText;
        set => SetProperty(ref _newFrameHeightText, value);
    }

    private string? _designMessage;
    /// <summary>Feedback for the design actions (e.g. why a frame or division couldn't be created).</summary>
    public string? DesignMessage
    {
        get => _designMessage;
        set
        {
            if (SetProperty(ref _designMessage, value))
                OnPropertyChanged(nameof(HasDesignMessage));
        }
    }

    public bool HasDesignMessage => !string.IsNullOrEmpty(_designMessage);

    private string? _hint;
    /// <summary>Transient status-bar feedback, e.g. the position while dragging a mullion.</summary>
    public string? Hint
    {
        get => _hint;
        set => SetProperty(ref _hint, value);
    }

    /// <summary>Creates a frame from the width/height fields, to the right of any existing frames.</summary>
    public void CreateFrame()
    {
        if (!PropertiesViewModel.TryParse(NewFrameWidthText, out double width)
            || !PropertiesViewModel.TryParse(NewFrameHeightText, out double height))
        {
            DesignMessage = "Enter the width and height as numbers in mm.";
            return;
        }

        double x = Project.Frames.Count == 0 ? 0 : Project.Frames.Max(f => f.X + f.Width) + Rules.FrameSpacingMm;
        if (Run(() => Core.Commands.CreateFrameCommand.Create(Project, x, 0, width, height, Rules)) is { } command)
        {
            Select(command.Frame.Id);
            Canvas.FitToContent();
        }
    }

    /// <summary>
    /// Adds a mullion/transom. With a glass panel selected it splits that opening only; otherwise it spans
    /// the whole opening of the selected (or only) frame, placed in the widest free space.
    /// </summary>
    public void AddDivision(MemberAxis axis)
    {
        Guid? selected = _selectedIds.Count == 1 ? _selectedIds.First() : null;
        Frame? frame = selected is { } id ? FindFrameOf(id) : null;
        frame ??= Project.Frames.Count == 1 ? Project.Frames[0] : null;
        if (frame is null)
        {
            DesignMessage = Project.Frames.Count == 0
                ? "Create a frame first."
                : "Select a frame or a glass panel first.";
            return;
        }

        Guid? splitGlass = selected is { } s && FindObject(s) is GlassPanel ? s : null;
        if (Run(() => new AddDivisionCommand(frame, axis, splitGlass, position: null, Rules)) is { CreatedProfileId: { } newId })
            Select(newId);
    }

    /// <summary>Deletes the selected division or frame. Glass is derived, so it can't be deleted on its own.</summary>
    public void DeleteSelection()
    {
        if (_selectedIds.Count != 1) return;
        Guid id = _selectedIds.First();

        switch (FindObject(id))
        {
            case Frame frame:
                Run(() => new DeleteFrameCommand(Project, frame));
                break;
            case Profile profile when Members.IsDivision(profile) && FindFrameOf(id) is { } owner:
                Run(() => new DeleteDivisionCommand(owner, id, Rules));
                break;
            case GlassPanel:
                DesignMessage = "Glass is created from the frame layout. Delete the mullion or transom beside it instead.";
                break;
        }
    }

    private string? ResizeSelectedFrame(double width, double height)
    {
        if (_selectedIds.Count != 1 || FindObject(_selectedIds.First()) is not Frame frame)
            return "Select a frame first.";
        return RunForMessage(() => new ResizeFrameCommand(frame, width, height, Rules));
    }

    private string? MoveSelectedDivision(double position)
    {
        if (_selectedIds.Count != 1 || FindObject(_selectedIds.First()) is not Profile profile
            || !Members.IsDivision(profile) || FindFrameOf(profile.Id) is not { } frame)
            return "Select a mullion or transom first.";
        return RunForMessage(() => new MoveDivisionCommand(frame, profile.Id, position, Rules));
    }

    /// <summary>Executes a command through the history; on a validation failure shows the reason and returns null.</summary>
    private T? Run<T>(Func<T> createCommand) where T : class, IUndoableCommand
    {
        try
        {
            var command = createCommand();
            CommandHistory.Execute(command);
            DesignMessage = null;
            return command;
        }
        catch (DesignValidationException ex)
        {
            DesignMessage = ex.Message;
            return null;
        }
    }

    private string? RunForMessage<T>(Func<T> createCommand) where T : class, IUndoableCommand
    {
        try
        {
            CommandHistory.Execute(createCommand());
            return null;
        }
        catch (DesignValidationException ex)
        {
            return ex.Message;
        }
    }

    // ── Status bar ──────────────────────────────────────────────────

    /// <summary>Project summary for the status bar. Cursor position and zoom come from <see cref="Canvas"/>.</summary>
    public string StatusText
    {
        get
        {
            int frames = Project.Frames.Count;
            int profiles = Project.Frames.Sum(f => f.Profiles.Count);
            int glass = Project.Frames.Sum(f => f.GlassPanels.Count);
            return $"Frames: {frames}  |  Profiles: {profiles}  |  Glass: {glass}  |  " +
                   $"Snap: {(Canvas.SnapToGrid ? "ON" : "OFF")}";
        }
    }

    // ── IDesignService implementation ───────────────────────────────

    public Project GetCurrentProject() => Project;

    public Project GetProjectSnapshot() => ProjectSerializer.Snapshot(Project);

    public Frame? GetSelectedFrame()
    {
        return Project.Frames.FirstOrDefault(f => _selectedIds.Contains(f.Id));
    }

    public IReadOnlyList<Profile> GetAllProfiles()
    {
        return Project.Frames.SelectMany(f => f.Profiles).ToList().AsReadOnly();
    }

    public IReadOnlyList<GlassPanel> GetAllGlassPanels()
    {
        return Project.Frames.SelectMany(f => f.GlassPanels).ToList().AsReadOnly();
    }

    public IReadOnlyList<Frame> GetFrames()
    {
        return Project.Frames.AsReadOnly();
    }
}
