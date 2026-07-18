namespace FantasyBooks.Models;

public class CartLine
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Selected options for this line (empty when the product has none).</summary>
    public List<CartOptionSelection> SelectedOptions { get; set; } = [];
}
