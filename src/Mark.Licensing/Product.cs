namespace Mark.Licensing;

/// <summary>The product lines MARK is sold for. Each is licensed separately, with its own validity.</summary>
public enum Product
{
    Upvc,
    Aluminium
}

public static class Products
{
    public static IReadOnlyList<Product> All { get; } = new[] { Product.Upvc, Product.Aluminium };

    /// <summary>The name shown to people: "uPVC" or "Aluminium".</summary>
    public static string DisplayName(this Product product) => product switch
    {
        Product.Upvc => "uPVC",
        Product.Aluminium => "Aluminium",
        _ => product.ToString()
    };
}
