namespace Mark.Core.Models;

/// <summary>
/// Root domain object representing an entire fenestration project.
/// This is the top-level entity serialized to/from JSON.
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable project name.</summary>
    public string Name { get; set; } = "Untitled Project";

    /// <summary>
    /// The unit system used for all dimensions.
    /// Always "mm" in the current version.
    /// </summary>
    public string Units { get; set; } = "mm";

    /// <summary>All frames in this project.</summary>
    public List<Frame> Frames { get; set; } = new();

    /// <summary>Extensible project-level metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>The quote this project is: number, status, client. Never null.</summary>
    public QuoteInfo Quote
    {
        get => _quote;
        set => _quote = value ?? new QuoteInfo();
    }

    private QuoteInfo _quote = new();

    /// <summary>How this quote is priced (cost heads, rates, discount, tax). Never null; files without one get the default.</summary>
    public PriceStructure Pricing
    {
        get => _pricing;
        set => _pricing = value ?? PriceStructure.Default();
    }

    private PriceStructure _pricing = PriceStructure.Default();

    /// <summary>How this quote uses the library's products (bar lengths, Products tab). Never null.</summary>
    public ProductSettings Products
    {
        get => _products;
        set => _products = value ?? new ProductSettings();
    }

    private ProductSettings _products = new();

    /// <summary>Creates a deep copy of this project. New Ids are assigned to all objects; the quote number is cleared.</summary>
    public Project Clone()
    {
        var clone = new Project
        {
            Id = Guid.NewGuid(),
            Name = Name,
            Units = Units,
            Metadata = new Dictionary<string, string>(Metadata),
            Quote = Quote.Copy(),
            Pricing = Pricing.Copy(),
            Products = Products.Copy()
        };
        clone.Quote.Number = "";   // a copy is a new quote: it gets its own number when saved

        foreach (var f in Frames)
            clone.Frames.Add(f.Clone());

        return clone;
    }
}
