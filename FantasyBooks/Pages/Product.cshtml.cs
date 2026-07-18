using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Pages;

public class ProductModel : PageModel
{
    private readonly LibraryContext _context;
    private readonly CartService _cart;
    private readonly TikTokOEmbedService _oEmbed;

    public ProductModel(LibraryContext context, CartService cart, TikTokOEmbedService oEmbed)
    {
        _context = context;
        _cart = cart;
        _oEmbed = oEmbed;
    }

    public Product? ProductDetail { get; private set; }

    /// <summary>Cover first (when present), then gallery image URLs for the detail carousel.</summary>
    public IReadOnlyList<string> GallerySrcs { get; private set; } = [];

    /// <summary>TikTok oEmbed HTML when the product has a video; otherwise null (section omitted).</summary>
    public string? TikTokEmbedHtml { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ProductDetail = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new Product
            {
                Id = p.Id,
                TikTokId = p.TikTokId,
                TikTokVideoUrl = p.TikTokVideoUrl,
                Name = p.Name,
                ImageUrl = p.ImageUrl,
                ImageContentType = p.ImageContentType,
                ImageRevision = p.ImageRevision,
                Description = p.Description,
                Price = p.Price,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ProductDetail is null)
            return NotFound();

        var srcs = new List<string>();
        var cover = ProductImageSrc.Resolve(ProductDetail);
        if (!string.IsNullOrWhiteSpace(cover))
            srcs.Add(cover);

        var galleryIds = await _context.ProductGalleryImages.AsNoTracking()
            .Where(g => g.ProductId == id)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        foreach (var gid in galleryIds)
            srcs.Add(ProductImageSrc.GallerySrc(id, gid));

        GallerySrcs = srcs;

        if (!string.IsNullOrWhiteSpace(ProductDetail.TikTokVideoUrl))
            TikTokEmbedHtml = await _oEmbed.GetEmbedHtmlAsync(ProductDetail.TikTokVideoUrl, cancellationToken);

        return Page();
    }

    public IActionResult OnPostAddToCart(int productId)
    {
        _cart.AddItem(productId, 1);
        return RedirectToPage("/Product", new { id = productId });
    }
}
