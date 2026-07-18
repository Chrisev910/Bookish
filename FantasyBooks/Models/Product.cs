namespace FantasyBooks.Models;

public class Product
{
    public int Id { get; set; }

    /// <summary>TikTok Shop product identifier — used for upserts and duplicate prevention.</summary>
    public string? TikTokId { get; set; }

    /// <summary>Optional TikTok video URL related to this product (shown on the product page when set).</summary>
    public string? TikTokVideoUrl { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>External image URL (TikTok CDN, etc.). Used when no uploaded file is stored.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>MIME type for <see cref="ImageData"/> (e.g. image/jpeg). Null when no file uploaded.</summary>
    public string? ImageContentType { get; set; }

    /// <summary>Uploaded image bytes persisted in the database (Turso/SQLite BLOB).</summary>
    public byte[]? ImageData { get; set; }

    /// <summary>Bumped when the cover image changes so browsers fetch a fresh <c>/media/products/{id}</c>.</summary>
    public int ImageRevision { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public ICollection<ProductGalleryImage> GalleryImages { get; set; } = [];

    public bool HasUploadedImage =>
        !string.IsNullOrWhiteSpace(ImageContentType) && ImageData is { Length: > 0 };
}
