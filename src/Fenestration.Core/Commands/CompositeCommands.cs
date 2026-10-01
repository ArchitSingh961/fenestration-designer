using Fenestration.Core.Design;
using Fenestration.Core.Models;

namespace Fenestration.Core.Commands;

/// <summary>
/// Several commands recorded as ONE undo step (e.g. moving two mullions in different frames, or deleting
/// a mixed selection). Executes in order and undoes in reverse order. If a child fails while executing,
/// the children already executed are undone before the exception propagates, so the model is never left
/// half-changed and nothing is recorded.
/// </summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    public CompositeCommand(string description, IEnumerable<IUndoableCommand> commands)
    {
        Description = description;
        _commands = commands?.ToList() ?? throw new ArgumentNullException(nameof(commands));
        if (_commands.Count == 0)
            throw new ArgumentException("A composite command needs at least one command.", nameof(commands));
    }

    public string Description { get; }

    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    public void Execute()
    {
        int done = 0;
        try
        {
            for (; done < _commands.Count; done++)
                _commands[done].Execute();
        }
        catch
        {
            for (int i = done - 1; i >= 0; i--)
                _commands[i].Undo();
            throw;
        }
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }

    /// <summary>The single command itself, or a composite when there are several.</summary>
    public static IUndoableCommand? Combine(string description, IReadOnlyList<IUndoableCommand> commands)
        => commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompositeCommand(description, commands)
        };
}

/// <summary>Moves a whole frame. Stores the old and new world position of its outer top-left corner.</summary>
public sealed class MoveFrameCommand : IUndoableCommand
{
    private readonly double _oldX, _oldY, _newX, _newY;

    public MoveFrameCommand(Frame frame, double newX, double newY)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        (_oldX, _oldY, _newX, _newY) = (frame.X, frame.Y, newX, newY);
    }

    public Frame Frame { get; }

    public string Description => $"Move frame to ({Members.Format(_newX)}, {Members.Format(_newY)}) mm";

    public void Execute() => FrameEditor.MoveFrame(Frame, _newX, _newY);

    public void Undo() => FrameEditor.MoveFrame(Frame, _oldX, _oldY);
}

/// <summary>Moves several divisions of one frame together (one validation, one undo step).</summary>
public sealed class MoveDivisionsCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<DivisionMove> _moves;

    public MoveDivisionsCommand(Frame frame, IReadOnlyList<DivisionMove> moves, DesignRules rules)
        : base(moves.Count == 1 ? $"Move division to {Members.Format(moves[0].Position)} mm" : $"Move {moves.Count} divisions", frame)
    {
        _rules = rules;
        _moves = moves.ToList();
    }

    public IReadOnlyList<DivisionMove> Moves => _moves;

    protected override void Apply(Frame frame) => FrameEditor.MoveDivisions(frame, _moves, _rules);
}

/// <summary>Deletes several divisions of one frame at once (e.g. a mullion together with the transoms ending on it).</summary>
public sealed class DeleteDivisionsCommand : FrameEditCommand
{
    private readonly DesignRules _rules;
    private readonly IReadOnlyList<Guid> _ids;

    public DeleteDivisionsCommand(Frame frame, IReadOnlyCollection<Guid> divisionIds, DesignRules rules)
        : base(divisionIds.Count == 1 ? "Delete division" : $"Delete {divisionIds.Count} divisions", frame)
    {
        _rules = rules;
        _ids = divisionIds.ToList();
    }

    protected override void Apply(Frame frame) => FrameEditor.DeleteDivisions(frame, _ids, _rules);
}
