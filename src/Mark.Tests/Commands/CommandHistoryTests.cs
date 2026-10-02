using Mark.Core.Commands;
using Mark.Core.Models;
using Xunit;

namespace Mark.Tests.Commands;

public class CommandHistoryTests
{
    /// <summary>
    /// Minimal test-only command. The real CreateFrameCommand arrives in Milestone 4;
    /// this verifies the history mechanics independently of it.
    /// </summary>
    private sealed class AddFrameTestCommand : IUndoableCommand
    {
        private readonly Project _project;
        private readonly Frame _frame;

        public AddFrameTestCommand(Project project, Frame frame)
        {
            _project = project;
            _frame = frame;
        }

        public string Description => "Add frame";
        public void Execute() => _project.Frames.Add(_frame);
        public void Undo() => _project.Frames.Remove(_frame);
    }

    [Fact]
    public void Create_Undo_Redo()
    {
        var project = new Project();
        var history = new CommandHistory();
        var frame = Frame.Create(0, 0, 1200, 1500);

        history.Execute(new AddFrameTestCommand(project, frame));
        Assert.Single(project.Frames);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        history.Undo();
        Assert.Empty(project.Frames);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Same(frame, Assert.Single(project.Frames));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NewCommand_ClearsRedoStack()
    {
        var project = new Project();
        var history = new CommandHistory();

        history.Execute(new AddFrameTestCommand(project, Frame.Create(0, 0, 100, 100)));
        history.Undo();
        history.Execute(new AddFrameTestCommand(project, Frame.Create(0, 0, 200, 200)));

        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void UndoRedo_OnEmptyHistory_AreNoOps()
    {
        var history = new CommandHistory();
        history.Undo();
        history.Redo();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void HistoryChanged_FiresOnEveryOperation()
    {
        var project = new Project();
        var history = new CommandHistory();
        int fired = 0;
        history.HistoryChanged += () => fired++;

        history.Execute(new AddFrameTestCommand(project, Frame.Create(0, 0, 100, 100)));
        history.Undo();
        history.Redo();
        history.Clear();

        Assert.Equal(4, fired);
    }
}
