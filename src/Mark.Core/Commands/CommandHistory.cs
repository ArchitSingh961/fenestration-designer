namespace Mark.Core.Commands;

/// <summary>
/// Manages the undo and redo stacks.
/// Commands are executed through this manager so that they are automatically tracked.
/// </summary>
public class CommandHistory : ICommandHistory
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    /// <summary>Fires after any command is executed, undone, or redone.</summary>
    public event Action? HistoryChanged;

    /// <summary>True if there are commands that can be undone.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>True if there are commands that can be redone.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Number of commands in the undo stack.</summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>Number of commands in the redo stack.</summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>Description of the next command to undo, or null.</summary>
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

    /// <summary>Description of the next command to redo, or null.</summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>
    /// Executes the command and pushes it onto the undo stack.
    /// Clears the redo stack (you can't redo after a new action).
    /// </summary>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        HistoryChanged?.Invoke();
    }

    /// <summary>Undoes the most recent command.</summary>
    public void Undo()
    {
        if (!CanUndo)
            return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        HistoryChanged?.Invoke();
    }

    /// <summary>Re-applies the most recently undone command.</summary>
    public void Redo()
    {
        if (!CanRedo)
            return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        HistoryChanged?.Invoke();
    }

    /// <summary>Clears both stacks (e.g. when loading a new project).</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        HistoryChanged?.Invoke();
    }
}
