namespace FantasyBooks.Models;

public class Product
{
    public int Id { get; set; }

    /// <summary>TikTok Shop product identifier — used for upserts and duplicate prevention.</summary>
    public string? TikTokId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }
}
