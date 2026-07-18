using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;

namespace FantasyBooks.Controllers;

/// <summary>Legacy route kept for old form posts; prefer Cart?handler=Checkout.</summary>
[Route("[controller]/[action]")]
public class StripeController(StripeCheckoutService checkout, CartService cart, ILogger<StripeController> logger)
    : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCheckoutSession(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Models.CartLine> lines;
            try
            {
                lines = cart.GetLines();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Session unavailable during legacy cart checkout.");
                TempData["CartError"] = "Your satchel session expired. Add items again, then checkout.";
                return RedirectToPage("/Cart");
            }

            var result = await checkout.CreateCartCheckoutAsync(lines, Request, cancellationToken);
            if (!result.Succeeded)
            {
                TempData["CartError"] = result.ErrorMessage;
                return RedirectToPage("/Cart");
            }

            return Redirect(result.RedirectUrl!);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error during legacy cart Stripe checkout.");
            TempData["CartError"] = "Checkout failed. Please try again.";
            return RedirectToPage("/Cart");
        }
    }
}
