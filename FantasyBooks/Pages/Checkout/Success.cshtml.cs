using FantasyBooks.Options;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Stripe.Checkout;

namespace FantasyBooks.Pages.Checkout;

public class SuccessModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly CartService _cart;

    public SuccessModel(IConfiguration configuration, CartService cart)
    {
        _configuration = configuration;
        _cart = cart;
    }

    public async Task OnGetAsync([FromQuery] string? session_id, CancellationToken cancellationToken)
    {
        var secretKey = StripeSecretResolver.ResolveSecretKey(_configuration);
        if (string.IsNullOrWhiteSpace(session_id) || string.IsNullOrWhiteSpace(secretKey))
            return;

        var client = new Stripe.StripeClient(secretKey);
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
