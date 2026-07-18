namespace FantasyBooks.Models;

/// <summary>Named option type for a product (e.g. Colour, Style).</summary>
public class ProductOptionGroup
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = "";

    public int SortOrder { get; set; }

    public ICollection<ProductOptionChoice> Choices { get; set; } = [];
}
