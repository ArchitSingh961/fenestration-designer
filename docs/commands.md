# Commands, Undo and Redo

## Abstractions (Core/Commands)

```csharp
public interface IUndoableCommand      // the designer command ("IDesignerCommand")
{
    string Description { get; }
    void Execute();
    void Undo();
}

public interface ICommandHistory       // implemented by CommandHistory
{
    bool CanUndo { get; }  bool CanRedo { get; }
    string? UndoDescription { get; }  string? RedoDescription { get; }
    event Action? HistoryChanged;
    void Execute(IUndoableCommand c);  void Undo();  void Redo();  void Clear();
}
```

The history is owned by `MainViewModel` (application layer), not by any window, and is never written to the project
file: undo history is session state. Commands reference domain objects only. `ArchitectureTests` fail if any command
type has a field holding a WPF type.

## Commands

| Command | Effect | How it undoes |
|---|---|---|
| `CreateFrameCommand` | adds a validated frame to the project | removes it |
| `DeleteFrameCommand` | removes a frame | re-inserts it at its original index |
| `MoveFrameCommand` | moves a frame's world position | stores old/new X, Y |
| `ResizeFrameCommand` | width/height (left/top anchored) | frame snapshot |
| `AddDivisionCommand` (`.Mullion`, `.Transom`) | adds a mullion/transom | frame snapshot |
| `MoveDivisionCommand` | one division to a position | frame snapshot |
| `MoveDivisionsCommand` | several divisions of one frame together | frame snapshot |
| `DeleteDivisionCommand` / `DeleteDivisionsCommand` | removes division(s) | frame snapshot |
| `FrameEditCommand.FromCompletedEdit` | wraps an edit that already happened | before/after snapshots |
| `CompositeCommand` | several commands as ONE step | children undone in reverse |

The spec's names map as follows: `DeleteElementCommand` is `DeleteFrameCommand` / `DeleteDivisionsCommand`,
possibly inside a `CompositeCommand`. `MoveElementCommand` is `MoveFrameCommand` / `MoveDivisionsCommand`.
`AddMullionCommand` / `AddTransomCommand` are `AddDivisionCommand.Mullion` / `.Transom`.

**Frame snapshots.** Frame edits have side effects: divisions ending on a moved member follow it, and glass is
re-derived. So `FrameEditCommand` records a `FrameSnapshot` (domain data, Ids preserved) before and after its first
execution. Undo restores "before" and redo restores "after": the same logical result, exactly, Ids included. These
are data mementos, not screenshots.

## Rules

- **Validation first.** A command whose `Execute` throws (`DesignValidationException`) is **not recorded**, and the
  model is unchanged. `FrameEditor` edits a copy and writes back only if valid. `CompositeCommand` undoes children it
  had already executed if a later one fails.
- **Redo branch.** A new command after an undo clears the redo stack: A → B → C, undo → B, do D gives **A → B → D**.
- **One drag = one command.** Tools preview during the drag and create exactly one command on release; a
  multi-object move or delete is one `CompositeCommand` (one Ctrl+Z).
- **Undo during a drag** cancels the drag first, so a preview is never applied on top of an undone model.
- **Examples:** width 1200 → resize 1400 → Ctrl+Z → 1200 → Ctrl+Y → 1400. Drag mullion 600 → 700 → Ctrl+Z → 600 →
  Ctrl+Y → 700.

## Calculation-engine compatibility

Commands run on mouse release, never per mouse move. A future calculation engine subscribes to
`ICommandHistory.HistoryChanged` (or `MainViewModel.OnDesignChanged`) and recalculates from
`IDesignService.GetProjectSnapshot()`. It never sees previews and needs no WPF.
