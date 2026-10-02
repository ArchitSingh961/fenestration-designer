using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Interaction;
using Mark.Core.Interfaces;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Core.Serialization;
using Mark.Core.Snapping;
using Mark.Core.Viewport;
using Mark.Designer.Interaction;
using Mark.Designer.Rendering;
using Mark.Designer.Tools;

namespace Mark.Designer.ViewModels;

/// <summary>A bill-of-materials row as shown in the panel.</summary>
public sealed record BomRow(string Category, string Name, string Detail, string Cost);

/// <summary>
/// Top-level view model (application layer for the designer). Owns the current project, selection,
/// command history, snapping, interaction mode/tools and the frame-designer actions.
///
/// Data flow for every edit: user input → tool / action → undoable command → <see cref="FrameEditor"/>
/// (validates, updates the domain model, re-derives glass) → history change → the renderer redraws from the
/// model. During a drag only a preview (<see cref="Interaction"/>) changes; the model changes once, on release.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDesignService
{
    /// <summary>
    /// Fit to Screen leaves this fraction of the viewport as margin: room for the dimensions,
    /// which are drawn a fixed pixel distance outside each frame.
    /// </summary>
    private const double FitMarginForDimensions = 0.25;

    private readonly Dictionary<InteractionMode, IViewportTool> _tools;
    private bool _refreshingProperties;

    /// <summary>A designer with an empty product library (geometry only; calculations report unassigned items).</summary>
    public MainViewModel() : this(ProductLibrary.Empty)
    {
    }

    /// <param name="library">The product library: glass, profiles and materials offered and priced.</param>
    /// <param name="calculationRules">Fabrication rules for the calculation (defaults if null).</param>
    public MainViewModel(IProductLibrary library, CalculationRules? calculationRules = null)
    {
        Library = library ?? throw new ArgumentNullException(nameof(library));
        Rules = RulesFor(library);
        Rules.Validate();
        Calculation = new CalculationService(() => Project, library, calculationRules);
        SnapSettings = new SnapSettings();
        SnapEngine = new SnapEngine(SnapSettings, Rules);
        Selection = new SelectionService();
        Interaction = new InteractionState();
        Canvas = new CanvasViewModel(new ViewportSettings { FitMarginFraction = FitMarginForDimensions })
        {
            GridSpacingMm = SnapSettings.GridSpacingMm,
            SnapToGrid = SnapSettings.GridEnabled
        };
        Properties = new PropertiesViewModel
        {
            ResizeFrame = ResizeSelectedFrame,
            MoveDivision = MoveSelectedDivision,
            AssignGlass = AssignGlass,
            AssignProfile = AssignProfile,
            Library = library,
            CalculationSource = () => Calculation.Result
        };
        CommandHistory = new CommandHistory();

        _tools = new Dictionary<InteractionMode, IViewportTool>
        {
            [InteractionMode.Select] = new SelectTool(this),
            [InteractionMode.Pan] = new PanTool(this),
            [InteractionMode.CreateFrame] = new FrameTool(this),
            [InteractionMode.AddMullion] = new DivisionTool(this, MemberAxis.Vertical),
            [InteractionMode.AddTransom] = new DivisionTool(this, MemberAxis.Horizontal)
        };

        NewProjectCommand = new RelayCommand(NewQuote);
        ResetViewCommand = Canvas.ResetViewCommand;
        UndoCommand = new RelayCommand(Undo, () => CommandHistory.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CommandHistory.CanRedo);
        CreateFrameCommand = new RelayCommand(CreateFrame);
        AddMullionCommand = new RelayCommand(() => AddDivision(MemberAxis.Vertical));
        AddTransomCommand = new RelayCommand(() => AddDivision(MemberAxis.Horizontal));
        DeleteSelectedCommand = new RelayCommand(DeleteSelection, () => HasSelection);
        SelectAllCommand = new RelayCommand(SelectAll);
        SetModeCommand = new RelayCommand(p =>
        {
            if (p is InteractionMode mode) Mode = mode;
            else if (p is string name && Enum.TryParse(name, out InteractionMode parsed)) Mode = parsed;
        });

        CommandHistory.HistoryChanged += () =>
        {
            ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
            IsDirty = true;
            OnDesignChanged();
        };
        CreatePersistenceCommands();
        CreateDesignFeatures();
        CreateQuoteFeatures();

        Selection.Changed += OnSelectionChanged;

        Interaction.Changed += previewChanged =>
        {
            if (previewChanged) Canvas.InvalidateContent();
            Canvas.InvalidateOverlay();
            var preview = Interaction.Preview;
            Hint = preview.Message;
            HintIsError = preview.Message is not null && !preview.IsValid;
        };

        Canvas.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CanvasViewModel.SnapToGrid):
                    SnapSettings.GridEnabled = Canvas.SnapToGrid;
                    OnPropertyChanged(nameof(StatusText));
                    break;
                case nameof(CanvasViewModel.GridSpacingMm):
                    SnapSettings.GridSpacingMm = Canvas.GridSpacingMm;
                    break;
            }
        };

        Canvas.ContentLayers.Add(new ProjectLayer(() => Project, IsSelected, PreviewFrameFor, () => IsOutsideView, Rules));
        Canvas.OverlayLayers.Add(new InteractionOverlayLayer(Interaction, () => SingleSelectedFrame));

        NewProject();
    }

    // ── Sub view models & services ──────────────────────────────────

    public CanvasViewModel Canvas { get; }
    public PropertiesViewModel Properties { get; }
    public CommandHistory CommandHistory { get; }
    public DesignRules Rules { get; }
    public ISelectionService Selection { get; }
    public SnapSettings SnapSettings { get; }
    public SnapEngine SnapEngine { get; }

    /// <summary>The product library (glass, profiles, materials) the design references by Id.</summary>
    public IProductLibrary Library { get; private set; }

    /// <summary>Keeps the calculation of the current design up to date (invalidated on every design change).</summary>
    public CalculationService Calculation { get; }

    /// <summary>
    /// Drawing defaults for new objects follow the library's default products, so a new frame is drawn with the
    /// face width of the profile it will be priced as. Without defaults the generic M4 values are kept.
    /// </summary>
    private static DesignRules RulesFor(IProductLibrary library)
    {
        var generic = new DesignRules();
        return new DesignRules
        {
            FrameThicknessMm = library.DefaultProfileFor(ProfileType.Frame)?.FaceWidthMm ?? generic.FrameThicknessMm,
            MullionThicknessMm = library.DefaultProfileFor(ProfileType.Mullion)?.FaceWidthMm ?? generic.MullionThicknessMm,
            TransomThicknessMm = library.DefaultProfileFor(ProfileType.Transom)?.FaceWidthMm ?? generic.TransomThicknessMm,
            DefaultGlassThicknessMm = library.DefaultGlass?.ThicknessMm ?? generic.DefaultGlassThicknessMm
        };
    }

    /// <summary>Preview/selection-box state of the interaction in progress (never domain data).</summary>
    public InteractionState Interaction { get; }

    /// <summary>The snap radius in world mm at the current zoom.</summary>
    public double SnapToleranceMm => SnapSettings.ToleranceMm(Canvas.ZoomLevel);

    /// <summary>Incremented whenever the design changes, so tools can drop cached state.</summary>
    public int DesignVersion { get; private set; }

    // ── Interaction mode ────────────────────────────────────────────

    private InteractionMode _mode = InteractionMode.Select;

    /// <summary>The active interaction mode. Switching cancels whatever the previous tool was doing.</summary>
    public InteractionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            ActiveTool.Cancel();
            Interaction.Clear();
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTool));
            OnPropertyChanged(nameof(ModeHint));
        }
    }

    /// <summary>The tool receiving left-button input in the viewport. The read-only Outside view only pans.</summary>
    public IViewportTool ActiveTool => _isOutsideView ? _tools[InteractionMode.Pan] : _tools[_mode];

    public IViewportTool ToolFor(InteractionMode mode) => _tools[mode];

    public string ModeHint => _isOutsideView ? OutsideViewHint : _mode switch
    {
        InteractionMode.Pan => "Pan: drag to move the view.",
        InteractionMode.CreateFrame => "Frame: drag a rectangle to draw a frame.",
        InteractionMode.AddMullion => "Mullion: click a glass panel (Shift = whole frame).",
        InteractionMode.AddTransom => "Transom: click a glass panel (Shift = whole frame).",
        _ => ""
    };

    public ICommand SetModeCommand { get; }

    // ── Snapping options ────────────────────────────────────────────

    public bool ObjectSnapEnabled
    {
        get => SnapSettings.ObjectSnapEnabled;
        set
        {
            if (SnapSettings.ObjectSnapEnabled == value) return;
            SnapSettings.ObjectSnapEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Snap radius in screen pixels (converted to mm at the current zoom).</summary>
    public double SnapTolerancePixels
    {
        get => SnapSettings.TolerancePixels;
        set
        {
            if (!double.IsFinite(value) || value < 0 || SnapSettings.TolerancePixels.Equals(value)) return;
            SnapSettings.TolerancePixels = value;
            OnPropertyChanged();
        }
    }

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

    public string Title => (_projectFilePath != null
        ? $"MARK — {System.IO.Path.GetFileName(_projectFilePath)}"
        : $"MARK — {QuoteHeader}") + (IsDirty ? " *" : "");

    /// <summary>
    /// Call after the committed design changed: redraws the content and refreshes the properties panel and
    /// status bar from the model.
    /// </summary>
    public void OnDesignChanged()
    {
        DesignVersion++;
        Calculation.Invalidate();
        Canvas.InvalidateContent();
        Canvas.InvalidateOverlay();
        RefreshProperties();
        RefreshCalculation();
        RefreshQuoteViews();
        OnPropertyChanged(nameof(StatusText));
    }

    // ── Selection (backed by ISelectionService; ids of domain objects) ─

    public IReadOnlySet<Guid> SelectedIds => Selection.SelectedIds;

    public bool HasSelection => Selection.Count > 0;

    public void Select(Guid id, bool addToSelection = false)
    {
        if (addToSelection) Selection.Add(id);
        else Selection.Select(id);
    }

    public void Deselect(Guid id) => Selection.Remove(id);

    public void ClearSelection() => Selection.Clear();

    public bool IsSelected(Guid id) => Selection.Contains(id);

    /// <summary>Selects every frame, division and glass panel (Ctrl+A).</summary>
    public void SelectAll() => Selection.SelectMany(SelectionQuery.All(Project));

    /// <summary>The selected frame when exactly one frame is selected (it shows resize handles).</summary>
    public Frame? SingleSelectedFrame
        => Selection.Count == 1 && FindObject(Selection.SelectedIds.First()) is Frame frame ? frame : null;

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIds));
        ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        RefreshProperties();
        Canvas.InvalidateContent();
        Canvas.InvalidateOverlay();
    }

    /// <summary>Rebuilds the properties panel from the current selection.</summary>
    private void RefreshProperties()
    {
        if (_refreshingProperties) return;
        _refreshingProperties = true;
        try
        {
            // Drop ids whose objects no longer exist (e.g. after undo of a create).
            Selection.Prune(id => FindObject(id) is not null);

            if (Selection.Count == 0)
                Properties.ShowNothing();
            else if (Selection.Count > 1)
                Properties.ShowMultiple(Selection.Count, SelectedObjectsInOrder());
            else
            {
                switch (FindObject(Selection.SelectedIds.First()))
                {
                    case Frame frame: Properties.ShowFrame(frame); break;
                    case Profile profile: Properties.ShowProfile(profile); break;
                    case GlassPanel glass: Properties.ShowGlass(glass); break;
                    default: Properties.ShowNothing(); break;
                }
            }
        }
        finally
        {
            _refreshingProperties = false;
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

    private Frame? PreviewFrameFor(Guid frameId)
        => Interaction.Preview.ReplacementFrames.TryGetValue(frameId, out var frame) ? frame : null;

    // ── Commands ────────────────────────────────────────────────────

    public ICommand NewProjectCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ResetViewCommand { get; }
    public ICommand CreateFrameCommand { get; }
    public ICommand AddMullionCommand { get; }
    public ICommand AddTransomCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }

    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
            return;
        ShowProject(new Project { Name = "New quote" });
    }

    /// <summary>
    /// Makes <paramref name="project"/> the open design (new, opened or imported): any drag is abandoned, the undo
    /// history and selection are cleared (they belong to the previous design) and the view is fitted.
    /// </summary>
    private void ShowProject(Project project)
    {
        ActiveTool.Cancel();
        Interaction.Clear();
        Project = project;
        CommandHistory.Clear();
        ClearSelection();
        ProjectFilePath = null;
        DesignMessage = null;
        IsDirty = false;
        Details.Load(project);
        RefreshQuoteViews();
        Canvas.InvalidateContent();
        Canvas.FitToContent();
    }

    /// <summary>Undo/redo first abandon any drag in progress, whose preview would otherwise be stale.</summary>
    private void Undo()
    {
        ActiveTool.Cancel();
        CommandHistory.Undo();
    }

    private void Redo()
    {
        ActiveTool.Cancel();
        CommandHistory.Redo();
    }

    /// <summary>
    /// Runs a command through the history. On a validation failure nothing is recorded, the model is unchanged,
    /// the reason is shown in <see cref="DesignMessage"/> and returned; null means success.
    /// </summary>
    public string? Execute(IUndoableCommand command)
    {
        try
        {
            CommandHistory.Execute(command);
            DesignMessage = null;
            return null;
        }
        catch (DesignValidationException ex)
        {
            DesignMessage = ex.Message;
            return ex.Message;
        }
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
    /// <summary>Transient status-bar feedback from the interaction in progress (position, snap, or why it's invalid).</summary>
    public string? Hint
    {
        get => _hint;
        set => SetProperty(ref _hint, value);
    }

    private bool _hintIsError;
    /// <summary>True when <see cref="Hint"/> explains why the current candidate is invalid.</summary>
    public bool HintIsError
    {
        get => _hintIsError;
        private set => SetProperty(ref _hintIsError, value);
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
        Guid? selected = Selection.Count == 1 ? Selection.SelectedIds.First() : null;
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

    /// <summary>
    /// Deletes the selection as one undo step: selected frames (with everything in them) and selected divisions
    /// of other frames. Glass is derived, so it can't be deleted on its own. If the remaining structure would be
    /// invalid (e.g. a transom left hanging), nothing is deleted and the reason is shown.
    /// </summary>
    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var ids = Selection.SelectedIds.ToHashSet();

        var frames = Project.Frames.Where(f => ids.Contains(f.Id)).ToList();
        var commands = new List<IUndoableCommand>();
        commands.AddRange(frames.Select(f => new DeleteFrameCommand(Project, f)));
        foreach (var frame in Project.Frames.Except(frames))
        {
            var divisions = frame.Profiles.Where(p => Members.IsDivision(p) && ids.Contains(p.Id)).Select(p => p.Id).ToList();
            if (divisions.Count > 0)
                commands.Add(new DeleteDivisionsCommand(frame, divisions, Rules));
        }

        if (commands.Count == 0)
        {
            if (ids.Any(id => FindObject(id) is GlassPanel))
                DesignMessage = "Glass is created from the frame layout. Delete the mullion or transom beside it instead.";
            return;
        }

        int count = commands.Count;
        Execute(CompositeCommand.Combine($"Delete {count} objects", commands)!);
    }

    private string? ResizeSelectedFrame(double width, double height)
    {
        if (SingleSelectedFrame is not { } frame)
            return "Select a frame first.";
        return RunForMessage(() => new ResizeFrameCommand(frame, width, height, Rules));
    }

    private string? MoveSelectedDivision(double position)
    {
        if (Selection.Count != 1 || FindObject(Selection.SelectedIds.First()) is not Profile profile
            || !Members.IsDivision(profile) || FindFrameOf(profile.Id) is not { } frame)
            return "Select a mullion or transom first.";
        return RunForMessage(() => new MoveDivisionCommand(frame, profile.Id, position, Rules));
    }

    // ── Library assignments (material change after design) ──────────

    /// <summary>
    /// Gives a library glass type to every glass panel the selection stands for: a selected panel itself, a
    /// selected frame all of its panels. Works for any selection (one pane, a box selection, Ctrl+A) and runs as
    /// ONE undoable step even across frames; if any frame rejects the change, nothing changes. The calculation,
    /// BOM and properties then update from the model. Returns an error message, or null on success.
    /// </summary>
    public string? AssignGlass(string definitionId)
    {
        static IEnumerable<Guid> Panels(Frame f) => f.GlassPanels.Select(g => g.Id);
        var targets = AssignmentTargets(Panels, Panels);
        if (targets.Count == 0)
            return "Select a glass panel or a frame first.";
        string name = Library.FindGlass(definitionId)?.Name ?? definitionId;
        return RunForMessage(() => CompositeCommand.Combine($"Change glass to {name}", targets
            .Select(t => (IUndoableCommand)new AssignGlassCommand(t.Frame, t.Ids, definitionId, Library, Rules)).ToList())!);
    }

    /// <summary>
    /// Makes every member the selection stands for from a library profile: a selected mullion/transom itself, a
    /// selected frame its four outer members. The face width comes from the library, so the glass is re-derived.
    /// One undoable step; all-or-nothing (e.g. a frame profile cannot be given to a mullion). Returns an error
    /// message, or null on success.
    /// </summary>
    public string? AssignProfile(string definitionId)
    {
        var targets = AssignmentTargets(f => f.Profiles.Select(p => p.Id),
            f => f.Profiles.Where(p => p.ProfileType == ProfileType.Frame).Select(p => p.Id));
        if (targets.Count == 0)
            return "Select a frame, mullion or transom first.";
        string name = Library.FindProfile(definitionId)?.Name ?? definitionId;
        return RunForMessage(() => CompositeCommand.Combine($"Change profile to {name}", targets
            .Select(t => (IUndoableCommand)new AssignProfileCommand(t.Frame, t.Ids, definitionId, Library, Rules)).ToList())!);
    }

    /// <summary>
    /// The objects the selection stands for, grouped by frame in project order: the selected ones among
    /// <paramref name="candidates"/>, plus <paramref name="ofSelectedFrame"/> of each selected frame.
    /// </summary>
    private List<(Frame Frame, IReadOnlyList<Guid> Ids)> AssignmentTargets(
        Func<Frame, IEnumerable<Guid>> candidates, Func<Frame, IEnumerable<Guid>> ofSelectedFrame)
    {
        var targets = new List<(Frame, IReadOnlyList<Guid>)>();
        foreach (var frame in Project.Frames)
        {
            var ids = candidates(frame).Where(Selection.Contains)
                .Concat(Selection.Contains(frame.Id) ? ofSelectedFrame(frame) : Enumerable.Empty<Guid>())
                .Distinct()
                .ToList();
            if (ids.Count > 0)
                targets.Add((frame, ids));
        }
        return targets;
    }

    /// <summary>The selected frames, profiles and glass panels, in project order (deterministic).</summary>
    private List<object> SelectedObjectsInOrder()
    {
        var objects = new List<object>();
        foreach (var frame in Project.Frames)
        {
            if (Selection.Contains(frame.Id)) objects.Add(frame);
            objects.AddRange(frame.Profiles.Where(p => Selection.Contains(p.Id)));
            objects.AddRange(frame.GlassPanels.Where(g => Selection.Contains(g.Id)));
        }
        return objects;
    }

    // ── Calculation summary (BOM and cost) ──────────────────────────

    /// <summary>The bill of materials of the whole project, formatted for the panel.</summary>
    public ObservableCollection<BomRow> BomRows { get; } = new();

    /// <summary>The cutting plan of the whole project (stock bars, remnants, waste), formatted for the panel.</summary>
    public CuttingPlanViewModel Cutting { get; } = new();

    private string _costText = "";
    /// <summary>Project total, e.g. "Total 24,310.50 INR".</summary>
    public string CostText
    {
        get => _costText;
        private set => SetProperty(ref _costText, value);
    }

    private string? _calculationStatus;
    /// <summary>Why the calculation is incomplete (e.g. "2 items could not be priced: …"), or null.</summary>
    public string? CalculationStatus
    {
        get => _calculationStatus;
        private set
        {
            if (SetProperty(ref _calculationStatus, value))
                OnPropertyChanged(nameof(HasCalculationStatus));
        }
    }

    public bool HasCalculationStatus => !string.IsNullOrEmpty(_calculationStatus);

    /// <summary>Reads the (re)calculated result and cutting plan, and refreshes the BOM rows, totals and plan.</summary>
    private void RefreshCalculation()
    {
        var result = Calculation.Result;
        BomRows.Clear();
        foreach (var line in result.Bom)
        {
            string quantity = $"{line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {line.Unit}";
            string detail = string.IsNullOrEmpty(line.Description) ? quantity
                : line.Category is BomCategory.Glass ? $"{quantity} × {line.Description}" : line.Description;
            BomRows.Add(new BomRow(line.Category.ToString(), line.Name, detail, FormatMoney(line.Cost)));
        }

        CostText = Project.Frames.Count == 0 ? "" : $"Total {FormatMoney(result.Cost.Total)} {result.Currency}".TrimEnd();
        var errors = result.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        CalculationStatus = errors.Count switch
        {
            0 => null,
            1 => errors[0].Message,
            _ => $"{errors.Count} items could not be priced. {errors[0].Message}"
        };
        Cutting.Show(Project.Frames.Count == 0 ? CuttingPlan.Empty : Calculation.CuttingPlan);
    }

    private static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>Executes a command through the history; on a validation failure shows the reason and returns null.</summary>
    private T? Run<T>(Func<T> createCommand) where T : class, IUndoableCommand
    {
        try
        {
            var command = createCommand();
            return Execute(command) is null ? command : null;
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
                   $"Snap: {(SnapSettings.ObjectSnapEnabled ? "objects" : "off")}{(Canvas.SnapToGrid ? " + grid" : "")}";
        }
    }

    // ── IDesignService implementation ───────────────────────────────

    public Project GetCurrentProject() => Project;

    public Project GetProjectSnapshot() => ProjectSerializer.Snapshot(Project);

    public Frame? GetSelectedFrame()
    {
        return Project.Frames.FirstOrDefault(f => Selection.Contains(f.Id));
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
