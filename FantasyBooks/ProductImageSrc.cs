using FantasyBooks.Models;

namespace FantasyBooks;

/// <summary>Resolves the img src for a product: uploaded DB image wins over external URL.</summary>
public static class ProductImageSrc
{
    public static string? Resolve(Product product)
    {
        if (!string.IsNullOrWhiteSpace(product.ImageContentType))
            return $"/media/products/{product.Id}";

        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
            return product.ImageUrl.Trim();

        return null;
    }

    /// <summary>True when the product has either an upload or an external URL.</summary>
    public static bool HasImage(Product product) =>
        !string.IsNullOrWhiteSpace(product.ImageContentType)
        || !string.IsNullOrWhiteSpace(product.ImageUrl);
}
