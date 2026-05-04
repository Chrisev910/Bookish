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

    public CatalogModel(LibraryContext context, CartService cart)
    {
        _context = context;
        _cart = cart;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IList<Product> Products { get; private set; } = [];

    public string? FlashMessage { get; private set; }

    public async Task OnGetAsync()
    {
        FlashMessage = TempData["FlashMessage"] as string;

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
}
