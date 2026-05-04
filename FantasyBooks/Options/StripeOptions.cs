namespace FantasyBooks.Options;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Stripe secret API key (sk_live_… or sk_test_…).</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Publishable key for client-side Stripe.js (optional here).</summary>
    public string PublishableKey { get; set; } = "";

    /// <summary>Per-unit surcharge in cents for each bundle line item (shipping-related).</summary>
    public long HeavyItemShippingSurchargeCents { get; set; } = 599;
}
