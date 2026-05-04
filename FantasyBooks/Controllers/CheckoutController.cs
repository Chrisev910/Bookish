using FantasyBooks;
using FantasyBooks.Data;
using FantasyBooks.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace FantasyBooks.Controllers;

[Route("[controller]/[action]")]
public class CheckoutController : Controller
{
    private readonly LibraryContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(LibraryContext db, IConfiguration configuration, ILogger<CheckoutController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Starts Stripe Checkout for a single catalog product (Buy now).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyNow(int productId, CancellationToken cancellationToken)
    {
        var secretKey = StripeSecretResolver.ResolveSecretKey(_configuration);
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            TempData["FlashMessage"] =
                "Stripe is not configured. Set Stripe__SecretKey or STRIPE_SECRET_KEY (or STRIPE_SECRET_KEY_FILE) on the host, or Stripe:SecretKey via user secrets / appsettings.Development.local.json locally.";
            return RedirectToPage("/Catalog");
        }

        if (productId <= 0)
            return RedirectToPage("/Catalog");

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            TempData["FlashMessage"] = "That title could not be found in the library.";
            return RedirectToPage("/Catalog");
        }

        var unitMinor = (long)Math.Round(product.Price * 100m, MidpointRounding.AwayFromZero);
        if (unitMinor < 50)
            unitMinor = 50;

        var baseUrl = PublicBaseUrl.Resolve(_configuration, Request);
        var successUrl = $"{baseUrl}/Checkout/Success?session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{baseUrl}/Catalog";


        var tiktokId = string.IsNullOrWhiteSpace(product.TikTokId) ? "none" : product.TikTokId!;

        var lineItems = new List<SessionLineItemOptions>
        {
            new()
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "gbp",
                    UnitAmount = unitMinor,
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = product.Name,
                        Description = StripeDescription(product.Description),
                    },
                },
            },
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
            Metadata = new Dictionary<string, string>
            {
                ["checkout_source"] = "buy_now",
                ["tiktok_product_ids"] = tiktokId,
            },
        };

        var client = new StripeClient(secretKey);
        var service = new SessionService(client);
        try
        {
            var checkoutSession = await service.CreateAsync(options, cancellationToken: cancellationToken);
            if (string.IsNullOrEmpty(checkoutSession.Url))
            {
                TempData["FlashMessage"] = "Stripe did not return a checkout URL.";
                return RedirectToPage("/Catalog");
            }

            return Redirect(checkoutSession.Url);
        }
        catch (StripeException ex)
        {
            TempData["FlashMessage"] = $"Stripe: {ex.Message}";
            return RedirectToPage("/Catalog");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating Stripe buy-now session.");
            TempData["FlashMessage"] = "Checkout failed. Please try again.";
            return RedirectToPage("/Catalog");
        }
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
