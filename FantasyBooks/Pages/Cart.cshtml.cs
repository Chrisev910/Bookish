using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
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

    public async Task OnGetAsync()
    {
        CartError = TempData["CartError"]?.ToString();
        await LoadCartAsync();
    }

    private async Task LoadCartAsync()
    {
        var cartLines = _cart.GetLines();
        if (cartLines.Count == 0)
        {
            Lines = [];
            Subtotal = 0;
            return;
        }

        var ids = cartLines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var rows = new List<CartLineView>();
        foreach (var line in cartLines)
        {
            if (!products.TryGetValue(line.ProductId, out var p))
                continue;

            rows.Add(new CartLineView(p.Id, p.Name, line.Quantity, p.Price));
            Subtotal += p.Price * line.Quantity;
        }

        Lines = rows;
    }

    public record CartLineView(int ProductId, string Name, int Quantity, decimal UnitPrice);
}
