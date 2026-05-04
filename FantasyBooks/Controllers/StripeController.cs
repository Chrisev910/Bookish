using FantasyBooks;
using FantasyBooks.Data;
using FantasyBooks.Options;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace FantasyBooks.Controllers;

public class StripeController : Controller
{
    private readonly StripeOptions _stripe;
    private readonly CartService _cart;
    private readonly LibraryContext _db;

    public StripeController(
        IOptions<StripeOptions> stripeOptions,
        CartService cart,
        LibraryContext db)
    {
        _stripe = stripeOptions.Value;
        _cart = cart;
        _db = db;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCheckoutSession(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            TempData["CartError"] = "Stripe is not configured. Add Stripe:SecretKey to configuration.";
            return RedirectToPage("/Cart");
        }

        var lines = _cart.GetLines();
        if (lines.Count == 0)
        {
            TempData["CartError"] = "Your treasury is empty.";
            return RedirectToPage("/Cart");
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (products.Count == 0)
        {
            TempData["CartError"] = "No matching wares were found in the library.";
            return RedirectToPage("/Cart");
        }

        var lineItems = new List<SessionLineItemOptions>();
        var tiktokIds = new List<string>();

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                continue;

            var ttId = product.TikTokId;
            if (!string.IsNullOrWhiteSpace(ttId))
                tiktokIds.Add(ttId);

            var unitCents = (long)Math.Round(product.Price * 100m, MidpointRounding.AwayFromZero);
            if (unitCents < 50)
                unitCents = 50;

            lineItems.Add(new SessionLineItemOptions
            {
                Quantity = line.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "usd",
                    UnitAmount = unitCents,
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = product.Name,
                        Description = StripeDescription(product.Description),
                    },
                },
            });
        }

        if (lineItems.Count == 0)
        {
            TempData["CartError"] = "Could not build a checkout session from the cart.";
            return RedirectToPage("/Cart");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/Checkout/Success?session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{baseUrl}/Cart";

        var idBlob = string.Join('|', tiktokIds.Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(idBlob))
            idBlob = "none";

        var metadata = new Dictionary<string, string>
        {
            ["tiktok_product_ids"] = idBlob,
        };

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = lineItems,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ShippingAddressCollection = new SessionShippingAddressCollectionOptions
            {
                AllowedCountries = new List<string> { "US", "CA", "GB", "AU", "NZ", "IE" },
            },
            Metadata = metadata,
        };

        var client = new Stripe.StripeClient(_stripe.SecretKey);
        var service = new SessionService(client);
        Stripe.Checkout.Session checkoutSession;
        try
        {
            checkoutSession = await service.CreateAsync(options, cancellationToken: cancellationToken);
        }
        catch (StripeException ex)
        {
            TempData["CartError"] = $"Stripe: {ex.Message}";
            return RedirectToPage("/Cart");
        }

        if (string.IsNullOrEmpty(checkoutSession.Url))
        {
            TempData["CartError"] = "Stripe did not return a checkout URL.";
            return RedirectToPage("/Cart");
        }

        return Redirect(checkoutSession.Url);
    }

    private static string? StripeDescription(string? htmlDescription)
    {
        var plain = HtmlPlainText.FromHtml(htmlDescription);
        if (string.IsNullOrWhiteSpace(plain))
            return null;
        plain = plain.Replace('\n', ' ');
        return plain.Length <= 500 ? plain : plain[..497] + "…";
    }
}
