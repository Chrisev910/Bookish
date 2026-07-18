using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;

namespace FantasyBooks.Controllers;

/// <summary>Legacy route kept for old form posts; prefer Catalog?handler=BuyNow.</summary>
[Route("[controller]/[action]")]
public class CheckoutController(StripeCheckoutService checkout, ILogger<CheckoutController> logger) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyNow(int productId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await checkout.CreateBuyNowCheckoutAsync(productId, Request, cancellationToken);
            if (!result.Succeeded)
            {
                TempData["FlashMessage"] = result.ErrorMessage;
                return RedirectToPage("/Catalog");
            }

            return Redirect(result.RedirectUrl!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during legacy buy-now Stripe checkout.");
            TempData["FlashMessage"] = "Checkout failed. Please try again.";
            return RedirectToPage("/Catalog");
        }
    }
}
