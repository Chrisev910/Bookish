namespace FantasyBooks.Models;

/// <summary>Customer-selected option stored on a cart line (and sent to Stripe).</summary>
public class CartOptionSelection
{
    public string GroupName { get; set; } = "";

    public string ChoiceLabel { get; set; } = "";
}
