using Mark.Core.Design;
using Mark.Core.Models;
using Mark.Core.Quotes;

namespace Mark.Core.Commands;

/// <summary>Changes the project name and the quote's status, client and notes (see <see cref="QuoteEditor.TrySetQuote"/>).</summary>
public sealed class SetQuoteInfoCommand : IUndoableCommand
{
    private readonly Project _project;
    private readonly string _name;
    private readonly QuoteInfo _quote;
    private string? _oldName;
    private QuoteInfo? _oldQuote;

    public SetQuoteInfoCommand(Project project, string name, QuoteInfo quote)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _name = name ?? "";
        _quote = quote?.Copy() ?? throw new ArgumentNullException(nameof(quote));
    }

    public string Description => "Edit client and quote details";

    public void Execute()
    {
        var oldName = _project.Name;
        var oldQuote = _project.Quote.Copy();
        QuoteEditor.SetQuote(_project, _name, _quote);
        (_oldName, _oldQuote) = (oldName, oldQuote);
    }

    public void Undo()
    {
        if (_oldQuote is null)
            throw new InvalidOperationException("Cannot undo a command that has not been executed.");
        _project.Name = _oldName!;
        _project.Quote = _oldQuote.Copy();
    }
}

/// <summary>
/// Adds a copy of a design (frame) to the right of the existing ones: same size, divisions, openings, glass and
/// profiles, new Ids and the next free reference. Quantity, location and notes are copied.
/// </summary>
public static class DuplicateFrameCommand
{
    public static CreateFrameCommand Create(Project project, Frame frame, DesignRules rules)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rules);
        var copy = frame.Clone();
        copy.X = project.Frames.Count == 0 ? 0 : project.Frames.Max(f => f.X + f.Width) + rules.FrameSpacingMm;
        copy.Y = frame.Y;
        copy.Design.Reference = "";
        return new CreateFrameCommand(project, copy);
    }
}

/// <summary>Replaces the quote's price structure (see <see cref="PricingEditor.TrySetPricing"/>). One undo step.</summary>
/// <summary>Sets the quote's product settings (bar lengths, Products tab), validated. Undo restores the previous ones.</summary>
public sealed class SetProductSettingsCommand : IUndoableCommand
{
    private readonly Project _project;
    private readonly ProductSettings _settings;
    private ProductSettings? _old;

    public SetProductSettingsCommand(Project project, ProductSettings settings, string description = "Change the bar lengths")
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _settings = settings?.Copy() ?? throw new ArgumentNullException(nameof(settings));
        Description = description;
    }

    public string Description { get; }

    public void Execute()
    {
        if (_settings.Problem() is { } problem) throw new Design.DesignValidationException(problem);
        _old = _project.Products.Copy();
        _project.Products = _settings.Copy();
    }

    public void Undo()
    {
        if (_old is null)
            throw new InvalidOperationException("Cannot undo a command that has not been executed.");
        _project.Products = _old.Copy();
    }
}

public sealed class SetPricingCommand : IUndoableCommand
{
    private readonly Project _project;
    private readonly PriceStructure _pricing;
    private PriceStructure? _old;

    public SetPricingCommand(Project project, PriceStructure pricing)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _pricing = pricing?.Copy() ?? throw new ArgumentNullException(nameof(pricing));
    }

    public string Description => "Change the price structure";

    public void Execute()
    {
        var old = _project.Pricing.Copy();
        PricingEditor.SetPricing(_project, _pricing);
        _old = old;
    }

    public void Undo()
    {
        if (_old is null)
            throw new InvalidOperationException("Cannot undo a command that has not been executed.");
        _project.Pricing = _old.Copy();
    }
}
