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

    public IReadOnlyList<ProductOptionGroup> OptionGroups { get; private set; } = [];

    public string? OptionsError { get; private set; }

    /// <summary>TikTok oEmbed HTML when the product has a video; otherwise null (section omitted).</summary>
    public string? TikTokEmbedHtml { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        OptionsError = TempData["OptionsError"] as string;
        return await LoadPageAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostAddToCartAsync(int productId, CancellationToken cancellationToken)
    {
        var groups = await ProductOptionStore.LoadForProductAsync(_context, productId, cancellationToken);
        var choiceByGroup = new Dictionary<int, int>();
        foreach (var group in groups)
        {
            var key = $"option_{group.Id}";
            if (int.TryParse(Request.Form[key], out var choiceId) && choiceId > 0)
                choiceByGroup[group.Id] = choiceId;
        }

        var selections = ProductOptionStore.ResolveSelections(groups, choiceByGroup, out var error);
        if (selections is null)
        {
            TempData["OptionsError"] = error ?? "Please choose your options.";
            return RedirectToPage(new { id = productId });
        }

        _cart.AddItem(productId, 1, selections);
        TempData["FlashMessage"] = "Added to your satchel.";
        return RedirectToPage(new { id = productId });
    }

    private async Task<IActionResult> LoadPageAsync(int id, CancellationToken cancellationToken)
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
        OptionGroups = await ProductOptionStore.LoadForProductAsync(_context, id, cancellationToken);

        if (!string.IsNullOrWhiteSpace(ProductDetail.TikTokVideoUrl))
            TikTokEmbedHtml = await _oEmbed.GetEmbedHtmlAsync(ProductDetail.TikTokVideoUrl, cancellationToken);

        return Page();
    }
}
