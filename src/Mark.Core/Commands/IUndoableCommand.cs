namespace Mark.Core.Commands;

/// <summary>
/// A reversible command that modifies the domain model.
/// All state-changing operations must go through this interface
/// so that undo/redo works correctly.
/// </summary>
public interface IUndoableCommand
{
    /// <summary>Human-readable description of this command (for UI display).</summary>
    string Description { get; }

    /// <summary>Applies the command to the domain model.</summary>
    void Execute();

    /// <summary>Reverses the effect of <see cref="Execute"/>.</summary>
    void Undo();
}
