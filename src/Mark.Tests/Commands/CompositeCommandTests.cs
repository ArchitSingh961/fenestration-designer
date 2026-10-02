using Mark.Core.Commands;
using Mark.Core.Design;
using Mark.Core.Models;
using Xunit;

namespace Mark.Tests.Commands;

public class CompositeCommandTests
{
    private static readonly DesignRules Rules = new();

    private sealed class Recorder : IUndoableCommand
    {
        private readonly List<string> _log;
        private readonly string _name;
        private readonly bool _fail;

        public Recorder(List<string> log, string name, bool fail = false) => (_log, _name, _fail) = (log, name, fail);

        public string Description => _name;

        public void Execute()
        {
            if (_fail) throw new DesignValidationException($"{_name} failed");
            _log.Add("+" + _name);
        }

        public void Undo() => _log.Add("-" + _name);
    }

    [Fact]
    public void ExecutesInOrder_UndoesInReverse()
    {
        var log = new List<string>();
        var composite = new CompositeCommand("both", new[] { new Recorder(log, "a"), new Recorder(log, "b") });

        composite.Execute();
        composite.Undo();

        Assert.Equal(new[] { "+a", "+b", "-b", "-a" }, log);
    }

    [Fact]
    public void FailingChild_RollsBackAndIsNotRecorded()
    {
        var log = new List<string>();
        var history = new CommandHistory();
        var composite = new CompositeCommand("three", new[] { new Recorder(log, "a"), new Recorder(log, "b"), new Recorder(log, "c", fail: true) });

        Assert.Throws<DesignValidationException>(() => history.Execute(composite));

        Assert.Equal(new[] { "+a", "+b", "-b", "-a" }, log);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Combine_SingleCommandIsReturnedAsIs()
    {
        var one = new Recorder(new List<string>(), "a");
        Assert.Same(one, CompositeCommand.Combine("x", new[] { one }));
        Assert.Null(CompositeCommand.Combine("x", Array.Empty<IUndoableCommand>()));
        Assert.IsType<CompositeCommand>(CompositeCommand.Combine("x", new[] { one, one }));
    }

    [Fact]
    public void ResizeUndoRedo_1200_1400_1200_1400()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        ICommandHistory history = new CommandHistory();

        history.Execute(new ResizeFrameCommand(frame, 1400, 1500, Rules));
        Assert.Equal(1400.0, frame.Width);
        history.Undo();
        Assert.Equal(1200.0, frame.Width);
        history.Redo();
        Assert.Equal(1400.0, frame.Width);
    }

    [Fact]
    public void NewActionAfterUndo_ClearsTheRedoBranch()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        var history = new CommandHistory();

        history.Execute(new ResizeFrameCommand(frame, 1300, 1500, Rules));   // A
        history.Execute(new ResizeFrameCommand(frame, 1400, 1500, Rules));   // B
        history.Execute(new ResizeFrameCommand(frame, 1500, 1500, Rules));   // C
        history.Undo();                                                       // back to B
        history.Execute(new ResizeFrameCommand(frame, 1600, 1500, Rules));   // D

        Assert.False(history.CanRedo);
        Assert.Equal(1600.0, frame.Width);
        history.Undo();
        Assert.Equal(1400.0, frame.Width);   // B, not C
        history.Undo();
        Assert.Equal(1300.0, frame.Width);   // A
    }

    [Fact]
    public void DeleteDivisions_TogetherWithTheTransomEndingOnIt()
    {
        var frame = FrameEditor.CreateFrame(0, 0, 1200, 1500, Rules);
        var mullion = FrameEditor.AddDivision(frame, MemberAxis.Vertical, null, 600, Rules);
        var left = frame.GlassPanels.OrderBy(g => g.Boundary.Left).First();
        var transom = FrameEditor.AddDivision(frame, MemberAxis.Horizontal, left.Id, 750, Rules);
        var history = new CommandHistory();

        Assert.Throws<DesignValidationException>(() => history.Execute(new DeleteDivisionsCommand(frame, new[] { mullion }, Rules)));
        history.Execute(new DeleteDivisionsCommand(frame, new[] { mullion, transom }, Rules));

        Assert.Single(frame.GlassPanels);
        history.Undo();
        Assert.Equal(3, frame.GlassPanels.Count);
    }

    [Fact]
    public void MoveFrameCommand_StoresOldAndNewPosition()
    {
        var frame = FrameEditor.CreateFrame(100, 200, 1200, 1500, Rules);
        var command = new MoveFrameCommand(frame, 400, 250);
        command.Execute();
        Assert.Equal((400.0, 250.0), (frame.X, frame.Y));
        command.Undo();
        Assert.Equal((100.0, 200.0), (frame.X, frame.Y));
    }
}
