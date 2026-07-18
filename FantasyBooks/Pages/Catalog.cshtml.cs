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

        Products = await query.OrderBy(p => p.Name).ToListAsync();
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
            TempData["FlashMessage"] = "Checkout failed. Please try again.";
            return RedirectToPage();
        }
    }
}
