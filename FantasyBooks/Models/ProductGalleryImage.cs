namespace FantasyBooks.Models;

/// <summary>Extra product photo for the detail-page carousel (cover stays on <see cref="Product"/>).</summary>
public class ProductGalleryImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Display order (0 = first after the cover).</summary>
    public int SortOrder { get; set; }

    public string ContentType { get; set; } = "image/jpeg";

    public byte[] ImageData { get; set; } = [];
}
