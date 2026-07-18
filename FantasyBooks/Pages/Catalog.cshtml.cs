using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Pages;

public class CatalogModel : PageModel
{
    private readonly LibraryContext _context;
    private readonly CartService _cart;
    private readonly StripeCheckoutService _checkout;
    private readonly ILogger<CatalogModel> _logger;

    public CatalogModel(
        LibraryContext context,
        CartService cart,
        StripeCheckoutService checkout,
        ILogger<CatalogModel> logger)
    {
        _context = context;
        _cart = cart;
        _checkout = checkout;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IList<Product> Products { get; private set; } = [];

    public string? FlashMessage { get; private set; }

    public async Task OnGetAsync(string? error)
    {
        FlashMessage = TempData["FlashMessage"] as string;
        if (string.IsNullOrEmpty(FlashMessage) && !string.IsNullOrWhiteSpace(error))
            FlashMessage = error;

        var query = _context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(p => p.Name.ToLower().Contains(term.ToLower()));
        }

        // Project without ImageData BLOBs (served separately via /media/products/{id}).
        Products = await query
            .OrderBy(p => p.Name)
            .Select(p => new Product
            {
                Id = p.Id,
                TikTokId = p.TikTokId,
                Name = p.Name,
                ImageUrl = p.ImageUrl,
                ImageContentType = p.ImageContentType,
                Description = p.Description,
                Price = p.Price,
            })
            .ToListAsync();
    }

    public IActionResult OnPostAddToCartAsync(int productId, string? search)
    {
        _cart.AddItem(productId, 1);
        return string.IsNullOrWhiteSpace(search)
            ? RedirectToPage("/Catalog")
            : RedirectToPage("/Catalog", new { Search = search });
    }

    public async Task<IActionResult> OnPostBuyNowAsync(int productId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _checkout.CreateBuyNowCheckoutAsync(productId, Request, cancellationToken);
            if (!result.Succeeded)
            {
                TempData["FlashMessage"] = result.ErrorMessage;
                return RedirectToPage();
            }

            return Redirect(result.RedirectUrl!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during buy-now Stripe checkout.");
            TempData["FlashMessage"] = $"{ex.GetType().Name}: {ex.Message}";
            return RedirectToPage();
        }
    }
}
