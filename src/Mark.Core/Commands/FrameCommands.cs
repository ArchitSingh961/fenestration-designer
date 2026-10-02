using Mark.Core.Design;
using Mark.Core.Models;

namespace Mark.Core.Commands;

/// <summary>
/// Base for commands that edit one frame through <see cref="FrameEditor"/>.
/// The first <see cref="Execute"/> runs the edit (which validates and may throw
/// <see cref="DesignValidationException"/> without changing anything, in which case the command is not
/// recorded). It also captures before/after <see cref="FrameSnapshot"/>s of the domain state, so Undo and
/// Redo restore exactly, Ids included, however complex the edit's side effects (re-attached divisions,
/// re-derived glass).
/// </summary>
public abstract class FrameEditCommand : IUndoableCommand
{
    private FrameSnapshot? _before;
    private FrameSnapshot? _after;

    protected FrameEditCommand(string description, Frame frame)
    {
        Description = description;
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public string Description { get; }

    public Frame Frame { get; }

    /// <summary>Performs the edit on <paramref name="frame"/>. Called once; redo replays the captured result.</summary>
    protected abstract void Apply(Frame frame);

    public void Execute()
    {
        if (_after is not null)
        {
            _after.ApplyTo(Frame);
            return;
        }

        var before = FrameSnapshot.Capture(Frame);
        Apply(Frame);
        _before = before;
        _after = FrameSnapshot.Capture(Frame);
    }

    public void Undo()
    {
        if (_before is null)
            throw new InvalidOperationException("Cannot undo a command that has not been executed.");
        _before.ApplyTo(Frame);
    }

    /// <summary>
    /// Wraps an edit that already happened interactively (e.g. a mouse drag that updated the model live)
    /// so it can be recorded in the history as one step.
    /// </summary>
    public static FrameEditCommand FromCompletedEdit(string description, Frame frame, FrameSnapshot before, FrameSnapshot after)
        => new CompletedEditCommand(description, frame, before, after);

    private sealed class CompletedEditCommand : FrameEditCommand
    {
        public CompletedEditCommand(string description, Frame frame, FrameSnapshot before, FrameSnapshot after)
            : base(description, frame)
        {
            _before = before;
            _after = after;
        }

        protected override void Apply(Frame frame) { }
    }
}

public sealed class ResizeFrameCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly double _width;
    private readonly double _height;

    public ResizeFrameCommand(Frame frame, double width, double height, DesignRules rules)
        : base($"Resize frame to {Members.Format(width)} × {Members.Format(height)} mm", frame)
    {
        _rules = rules;
        _width = width;
        _height = height;
    }

    protected override void Apply(Frame frame) => FrameEditor.Resize(frame, _width, _height, _rules);
}

/// <summary>Adds a mullion or transom (see <see cref="FrameEditor.AddDivision"/>).</summary>
public sealed class AddDivisionCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly MemberAxis _axis;
    private readonly Guid? _splitGlassId;
    private readonly double? _position;

    public AddDivisionCommand(Frame frame, MemberAxis axis, Guid? splitGlassId, double? position, DesignRules rules)
        : base(axis == MemberAxis.Vertical ? "Add mullion" : "Add transom", frame)
    {
        _rules = rules;
        _axis = axis;
        _splitGlassId = splitGlassId;
        _position = position;
    }

    public static AddDivisionCommand Mullion(Frame frame, DesignRules rules, Guid? splitGlassId = null, double? x = null)
        => new(frame, MemberAxis.Vertical, splitGlassId, x, rules);

    public static AddDivisionCommand Transom(Frame frame, DesignRules rules, Guid? splitGlassId = null, double? y = null)
        => new(frame, MemberAxis.Horizontal, splitGlassId, y, rules);

    /// <summary>Id of the profile created by the first execution.</summary>
    public Guid? CreatedProfileId { get; private set; }

    protected override void Apply(Frame frame)
        => CreatedProfileId = FrameEditor.AddDivision(frame, _axis, _splitGlassId, _position, _rules);
}

public sealed class MoveDivisionCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly Guid _divisionId;
    private readonly double _position;

    public MoveDivisionCommand(Frame frame, Guid divisionId, double position, DesignRules rules)
        : base($"Move division to {Members.Format(position)} mm", frame)
    {
        _rules = rules;
        _divisionId = divisionId;
        _position = position;
    }

    protected override void Apply(Frame frame) => FrameEditor.MoveDivision(frame, _divisionId, _position, _rules);
}

public sealed class DeleteDivisionCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly Guid _divisionId;

    public DeleteDivisionCommand(Frame frame, Guid divisionId, DesignRules rules)
        : base("Delete division", frame)
    {
        _rules = rules;
        _divisionId = divisionId;
    }

    protected override void Apply(Frame frame) => FrameEditor.DeleteDivision(frame, _divisionId, _rules);
}

/// <summary>
/// Adds an already-built (validated) frame to the project. A frame without a design reference gets the next free
/// one (W1, W2, …).
/// </summary>
public sealed class CreateFrameCommand : IUndoableCommand
{
    private readonly Project _project;

    public CreateFrameCommand(Project project, Frame frame)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        if (string.IsNullOrWhiteSpace(Frame.Design.Reference))
            Frame.Design.Reference = FrameEditor.NextReference(project);
    }

    /// <summary>Builds the frame with <see cref="FrameEditor.CreateFrame"/> (validating it) and wraps it in a command.</summary>
    public static CreateFrameCommand Create(Project project, double x, double y, double width, double height, DesignRules rules)
        => new(project, FrameEditor.CreateFrame(x, y, width, height, rules));

    public Frame Frame { get; }

    public string Description => $"Create frame {Members.Format(Frame.Width)} × {Members.Format(Frame.Height)} mm";

    public void Execute() => _project.Frames.Add(Frame);

    public void Undo() => _project.Frames.Remove(Frame);
}

public sealed class DeleteFrameCommand : IUndoableCommand
{
    private readonly Project _project;
    private int _index = -1;

    public DeleteFrameCommand(Project project, Frame frame)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public Frame Frame { get; }

    public string Description => "Delete frame";

    public void Execute()
    {
        _index = _project.Frames.IndexOf(Frame);
        if (_index < 0)
            throw new InvalidOperationException("The frame is not part of the project.");
        _project.Frames.RemoveAt(_index);
    }

    public void Undo() => _project.Frames.Insert(Math.Min(_index, _project.Frames.Count), Frame);
}
