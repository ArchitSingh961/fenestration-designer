using Mark.Calculation;
using Mark.Core.Library;
using Mark.Core.Models;
using Mark.Tests.Library;
using Xunit;

namespace Mark.Tests.Calculation;

public class CalculationServiceTests
{
    [Fact]
    public void Result_IsCalculatedLazily_AndCached()
    {
        var (project, _) = TestLibrary.SingleFrame();
        var service = new CalculationService(() => project, TestLibrary.Create());
        Assert.True(service.IsStale);
        Assert.Equal(0, service.CalculationCount);

        var first = service.Result;
        Assert.Same(first, service.Result);
        Assert.False(service.IsStale);
        Assert.Equal(1, service.CalculationCount);
    }

    [Fact]
    public void Invalidate_MarksStale_AndRaisesEvents()
    {
        var (project, _) = TestLibrary.SingleFrame();
        var service = new CalculationService(() => project, TestLibrary.Create());
        int invalidated = 0;
        CalculationResult? recalculated = null;
        service.Invalidated += () => invalidated++;
        service.Recalculated += r => recalculated = r;

        var first = service.Result;
        service.Invalidate();
        Assert.True(service.IsStale);
        Assert.Equal(1, invalidated);

        var second = service.Result;
        Assert.NotSame(first, second);
        Assert.Same(second, recalculated);
    }

    [Fact]
    public void ReadsTheCurrentProjectFromTheSource()
    {
        Project project = new();
        var service = new CalculationService(() => project, TestLibrary.Create());
        Assert.Empty(service.Result.Glass);

        project = TestLibrary.SingleFrame().Project;
        service.Invalidate();
        Assert.Single(service.Result.Glass);
    }

    [Fact]
    public void UsesTheGivenEngineAndRules()
    {
        var engine = new RecordingEngine();
        var rules = new CalculationRules { GlassEdgeClearanceMm = 1 };
        var service = new CalculationService(() => new Project(), ProductLibrary.Empty, rules, engine);

        _ = service.Result;
        Assert.Same(rules, engine.Rules);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CalculationService(() => new Project(), ProductLibrary.Empty, new CalculationRules { LengthDecimals = -1 }));
    }

    private sealed class RecordingEngine : ICalculationEngine
    {
        public CalculationRules? Rules { get; private set; }

        public CalculationResult Calculate(Project project, IProductLibrary library, CalculationRules rules)
        {
            Rules = rules;
            return CalculationResult.Empty;
        }
    }
}
