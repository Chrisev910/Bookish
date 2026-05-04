using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Pages;

public class CartModel : PageModel
{
    private readonly CartService _cart;
    private readonly LibraryContext _db;

    public CartModel(CartService cart, LibraryContext db)
    {
        _cart = cart;
        _db = db;
    }

    public string? CartError { get; private set; }

    public IReadOnlyList<CartLineView> Lines { get; private set; } = [];

    public decimal Subtotal { get; private set; }

    /// <summary>True when the session lists product IDs that no longer exist in the library (e.g. after re-import).</summary>
    public bool HasInvalidLines { get; private set; }

    public async Task OnGetAsync()
    {
        CartError = TempData["CartError"]?.ToString();
        await LoadCartAsync();
    }

    public IActionResult OnPostRemoveAsync(int productId)
    {
        _cart.RemoveItem(productId);
        return RedirectToPage();
    }

    private async Task LoadCartAsync()
    {
        Subtotal = 0;
        HasInvalidLines = false;

        var cartLines = _cart.GetLines();
        if (cartLines.Count == 0)
        {
            Lines = [];
            return;
        }

        var ids = cartLines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var rows = new List<CartLineView>();
        foreach (var line in cartLines)
        {
            if (!products.TryGetValue(line.ProductId, out var p))
            {
                HasInvalidLines = true;
                rows.Add(new CartLineView(line.ProductId, "No longer in the library (remove to continue)", line.Quantity, 0m, true));
                continue;
            }

            rows.Add(new CartLineView(p.Id, p.Name, line.Quantity, p.Price, false));
            Subtotal += p.Price * line.Quantity;
        }

        Lines = rows;
    }

    public record CartLineView(int ProductId, string Name, int Quantity, decimal UnitPrice, bool IsUnavailable = false);
}
