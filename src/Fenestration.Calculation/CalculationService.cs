using Fenestration.Core.Library;
using Fenestration.Core.Models;

namespace Fenestration.Calculation;

/// <summary>
/// Keeps the calculation of the current design up to date: the host calls <see cref="Invalidate"/> whenever the
/// committed design changes (e.g. on <c>ICommandHistory.HistoryChanged</c>, which covers edits, material changes,
/// undo and redo) and reads <see cref="Result"/>, which recalculates only if something changed since the last run.
/// <see cref="CuttingPlan"/> is derived from <see cref="Result"/> the same way: on first read after a change.
/// No UI dependency, so a server or batch process can use it the same way.
/// </summary>
public sealed class CalculationService
{
    private readonly Func<Project> _projectSource;
    private readonly ICalculationEngine _engine;
    private readonly ICuttingOptimizer _optimizer;
    private CalculationResult _result = CalculationResult.Empty;
    private CuttingPlan? _cuttingPlan;

    /// <param name="projectSource">Returns the design to calculate. The engine only reads it.</param>
    public CalculationService(Func<Project> projectSource, IProductLibrary library, CalculationRules? rules = null,
        ICalculationEngine? engine = null, ICuttingOptimizer? optimizer = null)
    {
        _projectSource = projectSource ?? throw new ArgumentNullException(nameof(projectSource));
        Library = library ?? throw new ArgumentNullException(nameof(library));
        Rules = rules ?? new CalculationRules();
        Rules.Validate();
        _engine = engine ?? new CalculationEngine();
        _optimizer = optimizer ?? new CuttingOptimizer();
    }

    /// <summary>The library the next calculation uses. Changed with <see cref="UseLibrary"/>.</summary>
    public IProductLibrary Library { get; private set; }

    public CalculationRules Rules { get; }

    /// <summary>
    /// Switches to a new library snapshot (e.g. after a product was edited in the library manager) and invalidates
    /// the result, so the BOM, cost and cutting plan are recalculated from the new definitions on the next read.
    /// </summary>
    public void UseLibrary(IProductLibrary library)
    {
        Library = library ?? throw new ArgumentNullException(nameof(library));
        Invalidate();
    }

    /// <summary>True when the design changed after the last calculation.</summary>
    public bool IsStale { get; private set; } = true;

    /// <summary>How many times the engine has run (for diagnostics and tests).</summary>
    public int CalculationCount { get; private set; }

    /// <summary>How many times the cutting optimiser has run (for diagnostics and tests).</summary>
    public int OptimizationCount { get; private set; }

    /// <summary>Raised by <see cref="Invalidate"/>.</summary>
    public event Action? Invalidated;

    /// <summary>Raised after every recalculation with the new result.</summary>
    public event Action<CalculationResult>? Recalculated;

    /// <summary>The result for the current design, recalculated first if it is stale.</summary>
    public CalculationResult Result => IsStale ? Recalculate() : _result;

    /// <summary>
    /// The cutting plan for the current <see cref="Result"/>'s profile pieces, optimised on first read after a change
    /// and cached until the next one.
    /// </summary>
    public CuttingPlan CuttingPlan
    {
        get
        {
            var result = Result;                 // recalculates (and drops the cached plan) if stale
            if (_cuttingPlan is null)
            {
                _cuttingPlan = _optimizer.Optimize(result, Library, Rules);
                OptimizationCount++;
            }
            return _cuttingPlan;
        }
    }

    /// <summary>Marks the result as out of date. Cheap: nothing is calculated until <see cref="Result"/> is read.</summary>
    public void Invalidate()
    {
        IsStale = true;
        Invalidated?.Invoke();
    }

    /// <summary>Calculates now, whether or not the design changed.</summary>
    public CalculationResult Recalculate()
    {
        _result = _engine.Calculate(_projectSource(), Library, Rules);
        _cuttingPlan = null;
        IsStale = false;
        CalculationCount++;
        Recalculated?.Invoke(_result);
        return _result;
    }
}
