namespace Mark.Core.Commands;

/// <summary>
/// Session undo/redo history. Holds <see cref="IUndoableCommand"/>s (the designer's command abstraction:
/// <c>Execute</c> / <c>Undo</c> on domain objects only, never UI objects).
///
/// Behaviour: executing a new command after an undo discards the redo branch (A → B → C, undo → B, do D
/// gives A → B → D). A command whose <c>Execute</c> throws is not recorded. The history belongs to the
/// editing session and is never saved in the project file.
/// </summary>
public interface ICommandHistory
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    string? UndoDescription { get; }
    string? RedoDescription { get; }

    /// <summary>Raised after execute, undo, redo or clear.</summary>
    event Action? HistoryChanged;

    void Execute(IUndoableCommand command);
    void Undo();
    void Redo();
    void Clear();
}
