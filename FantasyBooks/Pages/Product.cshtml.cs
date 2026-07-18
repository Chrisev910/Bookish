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

    public ProductModel(LibraryContext context, CartService cart)
    {
        _context = context;
        _cart = cart;
    }

    public Product? ProductDetail { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ProductDetail = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
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
            .FirstOrDefaultAsync(cancellationToken);

        return ProductDetail is null ? NotFound() : Page();
    }

    public IActionResult OnPostAddToCart(int productId)
    {
        _cart.AddItem(productId, 1);
        return RedirectToPage("/Product", new { id = productId });
    }
}
