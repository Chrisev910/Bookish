using FantasyBooks.Options;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Stripe.Checkout;

namespace FantasyBooks.Pages.Checkout;

public class SuccessModel : PageModel
{
    private readonly StripeOptions _stripe;
    private readonly CartService _cart;

    public SuccessModel(IOptions<StripeOptions> stripeOptions, CartService cart)
    {
        _stripe = stripeOptions.Value;
        _cart = cart;
    }

    public async Task OnGetAsync([FromQuery] string? session_id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session_id) || string.IsNullOrWhiteSpace(_stripe.SecretKey))
            return;

        var client = new Stripe.StripeClient(_stripe.SecretKey);
        var service = new SessionService(client);
        var checkoutSession = await service.GetAsync(session_id, cancellationToken: cancellationToken);

        if (!string.Equals(checkoutSession.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return;

        var source = checkoutSession.Metadata?.GetValueOrDefault("checkout_source");
        if (!string.Equals(source, "cart", StringComparison.OrdinalIgnoreCase))
            return;

        _cart.Clear();
    }
}
