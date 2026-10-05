namespace Mark.Core.Production;

/// <summary>The steps a window goes through in the workshop, in order.</summary>
public enum ProductionStep
{
    Cut,
    Assembled,
    Glazed,
    Ready,
    Dispatched
}

/// <summary>How far one design of a production order is: for each step, how many of its windows are done.</summary>
public sealed record WindowProgress
{
    public Guid FrameId { get; init; }

    /// <summary>Windows done per step (0 … the design's quantity).</summary>
    public IReadOnlyDictionary<ProductionStep, int> Done { get; init; } = new Dictionary<ProductionStep, int>();

    public int DoneAt(ProductionStep step) => Done.TryGetValue(step, out int n) ? n : 0;
}

/// <summary>
/// A production order (Milestone 16): the workshop's job for a confirmed order. It keeps the designs as they were when
/// production started (later changes to the quote do not change what is being made), the due date, notes and how far
/// each window is. Cutting lists, the glass order, the hardware pick list, shop drawings and labels are made from it.
/// </summary>
public sealed class ProductionOrder
{
    public static IReadOnlyList<ProductionStep> Steps { get; } = Enum.GetValues<ProductionStep>();

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The quote the order is.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>"OR-00012".</summary>
    public string OrderNumber { get; init; } = "";

    /// <summary>"QT-00031 R1".</summary>
    public string QuoteNumber { get; init; } = "";

    public string ProjectName { get; init; } = "";

    public string ClientName { get; init; } = "";

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = "";

    /// <summary>When the order should be ready (a date), or null.</summary>
    public DateTime? DueDate { get; set; }

    public string Notes { get; set; } = "";

    /// <summary>The designs as they were when production started (a project document, JSON); stored apart from the rest.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DocumentJson { get; set; } = "";

    /// <summary>Progress per design (frame id).</summary>
    public List<WindowProgress> Progress { get; set; } = new();

    /// <summary>When the cutting was done and the offcuts in stock updated (used ones taken out, new ones added), or null.</summary>
    public DateTime? OffcutsUpdatedUtc { get; set; }

    public WindowProgress ProgressOf(Guid frameId)
        => Progress.FirstOrDefault(p => p.FrameId == frameId) ?? new WindowProgress { FrameId = frameId };

    /// <summary>Sets how many windows of a design are done at a step (0 … <paramref name="quantity"/>).</summary>
    public void SetDone(Guid frameId, ProductionStep step, int count, int quantity)
    {
        var current = ProgressOf(frameId);
        var done = new Dictionary<ProductionStep, int>(current.Done) { [step] = Math.Clamp(count, 0, Math.Max(0, quantity)) };
        Progress.RemoveAll(p => p.FrameId == frameId);
        Progress.Add(current with { Done = done });
    }

    /// <summary>Done steps over all steps of all windows, 0–1.</summary>
    public double Fraction(IReadOnlyDictionary<Guid, int> quantities)
    {
        int total = quantities.Values.Sum() * Steps.Count;
        if (total == 0) return 0;
        int done = quantities.Sum(q => Steps.Sum(s => Math.Min(ProgressOf(q.Key).DoneAt(s), q.Value)));
        return (double)done / total;
    }

    /// <summary>"Dispatched" when every window is dispatched, else the furthest step every window has reached, or "Not started".</summary>
    public string StageText(IReadOnlyDictionary<Guid, int> quantities)
    {
        if (quantities.Count == 0) return "No windows";
        ProductionStep? reached = null;
        foreach (var step in Steps)
        {
            if (quantities.All(q => ProgressOf(q.Key).DoneAt(step) >= q.Value)) reached = step;
            else break;
        }
        if (reached is null)
            return Progress.Any(p => p.Done.Values.Any(v => v > 0)) ? "In production" : "Not started";
        return reached == ProductionStep.Dispatched ? "Dispatched" : $"All {StepName(reached.Value).ToLowerInvariant()}";
    }

    public static string StepName(ProductionStep step) => step switch
    {
        ProductionStep.Cut => "Cut",
        ProductionStep.Assembled => "Assembled",
        ProductionStep.Glazed => "Glazed",
        ProductionStep.Ready => "Ready",
        _ => "Dispatched"
    };
}

/// <summary>A reusable leftover bar kept in the workshop: which profile and how long.</summary>
public sealed record Offcut(long Id, string DefinitionId, double LengthMm, DateTime AddedUtc, string Source);
