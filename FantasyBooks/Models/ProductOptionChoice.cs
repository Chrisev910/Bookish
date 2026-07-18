namespace FantasyBooks.Models;

/// <summary>A selectable value within an option group (e.g. Red under Colour).</summary>
public class ProductOptionChoice
{
    public int Id { get; set; }

    public int GroupId { get; set; }

    public ProductOptionGroup? Group { get; set; }

    public string Label { get; set; } = "";

    public int SortOrder { get; set; }
}
