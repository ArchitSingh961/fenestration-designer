using Mark.Calculation;
using Mark.Core.Commands;
using Mark.Core.Models;
using Mark.Data;

namespace Mark.Designer.ViewModels;

/// <summary>
/// Milestone 11: the quote's price structure (Pricing tab), the company default for new quotes, and the priced quote
/// (<see cref="Price"/>) behind the header total, the design cards and the saved quote value.
/// </summary>
public partial class MainViewModel
{
    public PricingViewModel Pricing { get; private set; } = null!;

    private QuotePrice? _price;

    /// <summary>The open quote priced with its own price structure (recomputed after any change).</summary>
    public QuotePrice Price => _price ??= PricingEngine.Price(Project, Calculation.Result, Project.Pricing,
        Calculation.Rules.MoneyDecimals);

    private void CreatePricingFeatures()
    {
        Pricing = new PricingViewModel(() => Project, () => Calculation.Result, () => Library.Currency,
            pricing => RunForMessage(() => new SetPricingCommand(Project, pricing)),
            () => Store is null ? null : DefaultPricing(),
            SaveDefaultPricing,
            OpenLibraryManager)
        {
            LibrarySource = () => Library,
            CalculateWith = library => new CalculationEngine().Calculate(Project, library, Calculation.Rules),
            SetDesignExtraCost = SetDesignExtraCost
        };
    }

    /// <summary>A design's extra cost per window (Pricing › Design add-on cost heads): one undo step.</summary>
    private string? SetDesignExtraCost(Guid frameId, decimal amount)
    {
        if (Project.Frames.FirstOrDefault(f => f.Id == frameId) is not { } frame) return "The design no longer exists.";
        if (frame.Design.ExtraCost == amount) return null;
        var info = frame.Design.Copy();
        info.ExtraCost = amount;
        return RunForMessage(() => new SetDesignInfoCommand(frame, info));
    }

    /// <summary>Forgets the cached price; called whenever the design, the pricing or the library changes.</summary>
    private void InvalidatePrice() => _price = null;

    /// <summary>The company's default price structure (saved in the database), or the built-in one.</summary>
    public PriceStructure DefaultPricing()
    {
        try
        {
            return Store?.Settings.LoadDefaultPricing() ?? PriceStructure.Default();
        }
        catch (DataStoreException)
        {
            return PriceStructure.Default();
        }
    }

    private string? SaveDefaultPricing(PriceStructure pricing)
    {
        if (Store is null) return "There is no local database, so a default cannot be saved.";
        if (Access.ReadOnlyMessage is { } readOnly) return readOnly;
        try
        {
            Store.Settings.SaveDefaultPricing(pricing);
            return null;
        }
        catch (Exception ex) when (ex is DataStoreException or ArgumentException)
        {
            return ex.Message;
        }
    }
}
