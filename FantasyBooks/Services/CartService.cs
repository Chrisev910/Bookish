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
        if (list is null)
            return [];

        foreach (var line in list)
            line.SelectedOptions = ProductOptionsFormat.Normalize(line.SelectedOptions);

        return list;
    }

    /// <summary>Header badge count; never throws (layout runs on every page including /Error).</summary>
    public int GetSatchelDisplayCount()
    {
        try
        {
            return GetLines().Sum(l => l.Quantity);
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    public void SetLines(IReadOnlyList<CartLine> lines)
    {
        var normalized = lines
            .Where(l => l.ProductId > 0 && l.Quantity > 0)
            .Select(l => new CartLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                SelectedOptions = ProductOptionsFormat.Normalize(l.SelectedOptions),
            })
            .GroupBy(l => (l.ProductId, Key: ProductOptionsFormat.Signature(l.SelectedOptions)))
            .Select(g => new CartLine
            {
                ProductId = g.Key.ProductId,
                Quantity = g.Sum(x => x.Quantity),
                SelectedOptions = g.First().SelectedOptions,
            })
            .ToList();

        Session.SetString(SessionKey, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public void AddItem(int productId, int quantity = 1, IEnumerable<CartOptionSelection>? selectedOptions = null)
    {
        if (productId <= 0 || quantity <= 0)
            return;

        var opts = ProductOptionsFormat.Normalize(selectedOptions);
        var key = ProductOptionsFormat.Signature(opts);
        var lines = GetLines().ToList();
        var existing = lines.FirstOrDefault(l =>
            l.ProductId == productId
            && ProductOptionsFormat.Signature(l.SelectedOptions) == key);

        if (existing is null)
            lines.Add(new CartLine { ProductId = productId, Quantity = quantity, SelectedOptions = opts });
        else
            existing.Quantity += quantity;

        SetLines(lines);
    }

    public void RemoveItem(int productId, string? optionsKey = null)
    {
        var key = optionsKey ?? "";
        var lines = GetLines()
            .Where(l => !(l.ProductId == productId
                && ProductOptionsFormat.Signature(l.SelectedOptions) == key))
            .ToList();
        SetLines(lines);
    }

    public void Clear()
    {
        try
        {
            var http = httpContextAccessor.HttpContext;
            if (http?.Session is null)
                return;
            http.Session.Remove(SessionKey);
        }
        catch (InvalidOperationException)
        {
            // Session may be unavailable on some return-from-Stripe requests; clearing is best-effort.
        }
    }
}
