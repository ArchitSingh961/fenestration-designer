using System.Windows.Input;
using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Geometry;
using Mark.Core.Models;
using Mark.Designer.Interaction;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 9: the design library (apply by click or by dragging onto an opening), opening types, design details and
/// the Inside / Outside view.
/// </summary>
public partial class MainViewModel : IViewportDropTarget
{
    /// <summary>Grid a design dropped on empty space is placed on (top-left corner), in mm.</summary>
    private const double DropPlacementStepMm = 10.0;

    /// <summary>The design library panel (rail + designs).</summary>
    public DesignLibraryViewModel DesignLibrary { get; private set; } = null!;

    public ICommand ShowInsideCommand { get; private set; } = null!;
    public ICommand ShowOutsideCommand { get; private set; } = null!;

    private void CreateDesignFeatures()
    {
        DesignLibrary = new DesignLibraryViewModel(Rules, (template, systemId) => ApplyDesign(template, systemId));
        ShowInsideCommand = new RelayCommand(() => IsOutsideView = false);
        ShowOutsideCommand = new RelayCommand(() => IsOutsideView = true);
        Properties.SetDesignInfo = SetDesignInfo;
        Properties.SetOpening = SetOpening;
    }

    // ── Inside / Outside ────────────────────────────────────────────

    private bool _isOutsideView;

    /// <summary>
    /// True to draw the design as seen from outside (mirrored, hinges swapped). The Outside view is for checking and
    /// presenting: it is read-only, so the mouse only pans until the Inside view is shown again.
    /// </summary>
    public bool IsOutsideView
    {
        get => _isOutsideView;
        set
        {
            if (_isOutsideView == value) return;
            ActiveTool.Cancel();
            Interaction.Clear();
            _isOutsideView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInsideView));
            OnPropertyChanged(nameof(ActiveTool));
            OnPropertyChanged(nameof(ModeHint));
            Canvas.InvalidateContent();
        }
    }

    public bool IsInsideView
    {
        get => !_isOutsideView;
        set => IsOutsideView = !value;
    }

    private const string OutsideViewHint = "Outside view (as seen from outside, read-only). Switch to Inside to edit.";

    // ── Applying library designs ────────────────────────────────────

    /// <summary>
    /// Applies a library design to what is selected: each selected opening (glass panel), or each selected frame as a
    /// whole. With nothing selected it goes to the only frame, or, if there are no frames yet, a new frame of the
    /// design's suggested size is created with it. Replacing a whole frame's existing design asks first. One undo step.
    /// Returns an error message, or null (also when the user declined).
    /// </summary>
    /// <param name="systemId">A system of the company's own to make the frames in (null: they keep theirs).</param>
    public string? ApplyDesign(DesignTemplate template, string? systemId = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (IsOutsideView) return "Switch to the Inside view to change the design.";
        if (DesignBlocked(template) is { } blocked) return DesignMessage = blocked;

        var targets = new List<(Frame Frame, Guid? GlassId)>();
        foreach (var frame in Project.Frames)
        {
            if (Selection.Contains(frame.Id))
                targets.Add((frame, null));
            else
                targets.AddRange(frame.GlassPanels.Where(g => Selection.Contains(g.Id)).Select(g => (frame, (Guid?)g.Id)));
        }

        if (targets.Count == 0)
        {
            // A selected mullion/transom stands for its frame.
            var frames = Project.Frames.Where(f => f.Profiles.Any(p => Selection.Contains(p.Id))).ToList();
            if (frames.Count == 0 && Project.Frames.Count == 1) frames.Add(Project.Frames[0]);
            targets.AddRange(frames.Select(f => (f, (Guid?)null)));
        }

        if (targets.Count == 0)
        {
            if (Project.Frames.Count > 0) return "Select a frame or an opening first.";
            return CreateFrameWithDesign(template, null, systemId);
        }
        return ApplyDesign(template, targets, systemId);
    }

    private string? ApplyDesign(DesignTemplate template, IReadOnlyList<(Frame Frame, Guid? GlassId)> targets, string? systemId = null)
    {
        var replaced = targets.Where(t => t.GlassId is null && !template.KeepsLayout && HasDesign(t.Frame))
            .Select(t => Reference(t.Frame)).ToList();
        if (replaced.Count > 0 && Dialogs is not null
            && !Dialogs.Confirm("Replace design",
                $"The existing design of {string.Join(", ", replaced)} will be cleared. Do you want to proceed?"))
            return null;

        var commands = targets.Select(t => (IUndoableCommand)new ApplyTemplateCommand(t.Frame, template, t.GlassId, Rules)).ToList();
        if (systemId is not null)
            commands.AddRange(targets.Select(t => t.Frame).Distinct()
                .Select(f => (IUndoableCommand)new SetFrameSystemCommand(f, systemId, Library, Rules)));
        string? error = RunForMessage(() => CompositeCommand.Combine($"Apply design \"{template.Name}\"", commands)!);
        DesignMessage = error;
        return error;
    }

    /// <summary>True if the frame has anything a whole-frame design would clear: divisions, sashes or mesh.</summary>
    private static bool HasDesign(Frame frame)
        => frame.Profiles.Any(Members.IsDivision) || frame.GlassPanels.Any(g => g.Opening.IsOpenable() || g.HasMesh);

    private static string Reference(Frame frame)
        => string.IsNullOrWhiteSpace(frame.Design.Reference) ? "this frame" : frame.Design.Reference;

    /// <summary>
    /// A new frame of the design's suggested size with the design applied, at <paramref name="topLeft"/> or to the right
    /// of the existing frames. One undo step; the new frame is selected.
    /// </summary>
    private string? CreateFrameWithDesign(DesignTemplate template, Point2D? topLeft, string? systemId = null)
    {
        var (width, height) = template.SuggestedSize;
        double x = topLeft?.X ?? (Project.Frames.Count == 0 ? 0 : Project.Frames.Max(f => f.X + f.Width) + Rules.FrameSpacingMm);
        double y = topLeft?.Y ?? 0;

        CreateFrameCommand create;
        try
        {
            create = Core.Commands.CreateFrameCommand.Create(Project, x, y, width, height, Rules);
        }
        catch (DesignValidationException ex)
        {
            return DesignMessage = ex.Message;
        }

        var steps = new List<IUndoableCommand> { create };
        if (systemId is not null)
            steps.Add(new SetFrameSystemCommand(create.Frame, systemId, Library, Rules));
        if (!template.KeepsLayout)
            steps.Add(new ApplyTemplateCommand(create.Frame, template, null, Rules));
        string? error = RunForMessage(() => CompositeCommand.Combine($"New design \"{template.Name}\"", steps)!);
        DesignMessage = error;
        if (error is null)
        {
            Select(create.Frame.Id);
            if (topLeft is null) Canvas.FitToContent();
        }
        return error;
    }

    // ── Opening type and design details (properties panel) ──────────

    /// <summary>Sets type and/or mesh of panels (grouped by frame), as one undo step. Returns an error message or null.</summary>
    public string? SetOpening(IReadOnlyList<GlassPanel> panels, OpeningType? opening, bool? mesh)
    {
        if (IsOutsideView) return "Switch to the Inside view to change the design.";
        if (!Access.CanUseOpenings && (opening is not null and not OpeningType.Fixed || mesh == true))
            return Access.OpeningsLock;
        var ids = panels.Select(p => p.Id).ToHashSet();
        var commands = Project.Frames
            .Select(f => (Frame: f, Ids: f.GlassPanels.Where(g => ids.Contains(g.Id)).Select(g => g.Id).ToList()))
            .Where(t => t.Ids.Count > 0)
            .Select(t => (IUndoableCommand)new SetOpeningCommand(t.Frame, t.Ids, opening, mesh, Rules))
            .ToList();
        if (commands.Count == 0) return "Select an opening first.";
        return RunForMessage(() => CompositeCommand.Combine(commands[0].Description, commands)!);
    }

    private string? SetDesignInfo(DesignInfo info)
    {
        if (SingleSelectedFrame is not { } frame) return "Select a frame first.";
        return RunForMessage(() => new SetDesignInfoCommand(frame, info));
    }

    // ── Drag and drop onto the drawing ──────────────────────────────

    /// <summary>What a design dropped at <paramref name="world"/> would go to: an opening, a whole frame, or new space.</summary>
    private (Frame? Frame, Guid? GlassId, Rectangle2D? Highlight) DropTargetAt(Point2D world)
    {
        if (FrameHitTester.HitTest(Project, world, 0) is not { } hit)
            return (null, null, null);
        var frame = Project.Frames.First(f => f.Id == hit.FrameId);
        if (hit.Kind == DesignElementKind.Glass)
        {
            var glass = frame.GlassPanels.First(g => g.Id == hit.ElementId);
            return (frame, glass.Id, glass.Boundary.Offset(frame.X, frame.Y));
        }
        return (frame, null, frame.Bounds);
    }

    /// <summary>A dragged design: "template id", or "template id@system id" for one in the company's own system.</summary>
    private static (DesignTemplate? Template, string? SystemId) Dragged(string dragId)
    {
        int at = dragId.IndexOf('@');
        return at < 0 ? (DesignTemplates.Find(dragId), null) : (DesignTemplates.Find(dragId[..at]), dragId[(at + 1)..]);
    }

    bool IViewportDropTarget.DragOver(Point2D world, string templateId)
    {
        if (IsOutsideView || Dragged(templateId).Template is null)
        {
            Interaction.SetDropTarget(null);
            return false;
        }
        Interaction.SetDropTarget(DropTargetAt(world).Highlight);
        return true;
    }

    void IViewportDropTarget.DragLeave() => Interaction.SetDropTarget(null);

    bool IViewportDropTarget.Drop(Point2D world, string templateId)
    {
        Interaction.SetDropTarget(null);
        var (dragged, systemId) = Dragged(templateId);
        if (IsOutsideView || dragged is not { } template)
            return false;

        var (frame, glassId, _) = DropTargetAt(world);
        string? error = DesignBlocked(template) is { } blocked ? blocked
            : frame is null
            ? CreateFrameWithDesign(template, new Point2D(Snap(world.X), Snap(world.Y)), systemId)
            : ApplyDesign(template, new[] { (frame, glassId) }, systemId);
        DesignLibrary.Message = error;
        if (error is null && frame is not null)
            Select(glassId is { } id && frame.GlassPanels.Any(g => g.Id == id) ? id : frame.Id);
        return error is null;

        static double Snap(double v) => Math.Round(v / DropPlacementStepMm) * DropPlacementStepMm;
    }
}
