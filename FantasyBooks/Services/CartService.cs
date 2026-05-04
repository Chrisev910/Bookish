using System.Text.Json;
using FantasyBooks.Models;
using Microsoft.AspNetCore.Http;

namespace FantasyBooks.Services;

public class CartService(IHttpContextAccessor httpContextAccessor)
{
    private const string SessionKey = "cart.lines.v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private ISession Session =>
        httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("Session is not available.");

    public IReadOnlyList<CartLine> GetLines()
    {
        var raw = Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(raw))
            return [];

        var list = JsonSerializer.Deserialize<List<CartLine>>(raw, JsonOptions);
        return list ?? [];
    }

    public void SetLines(IReadOnlyList<CartLine> lines)
    {
        var normalized = lines
            .Where(l => l.ProductId > 0 && l.Quantity > 0)
            .GroupBy(l => l.ProductId)
            .Select(g => new CartLine { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        Session.SetString(SessionKey, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public void AddItem(int productId, int quantity = 1)
    {
        if (productId <= 0 || quantity <= 0)
            return;

        var lines = GetLines().ToList();
        var existing = lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is null)
            lines.Add(new CartLine { ProductId = productId, Quantity = quantity });
        else
            existing.Quantity += quantity;

        SetLines(lines);
    }

    public void SetQuantity(int productId, int quantity)
    {
        if (productId <= 0)
            return;

        var lines = GetLines().ToList();
        var existing = lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is null)
        {
            if (quantity > 0)
                lines.Add(new CartLine { ProductId = productId, Quantity = quantity });
        }
        else if (quantity <= 0)
        {
            lines.Remove(existing);
        }
        else
        {
            existing.Quantity = quantity;
        }

        SetLines(lines);
    }

    public void RemoveItem(int productId)
    {
        var lines = GetLines().Where(l => l.ProductId != productId).ToList();
        SetLines(lines);
    }

    public void Clear()
    {
        Session.Remove(SessionKey);
    }
}
