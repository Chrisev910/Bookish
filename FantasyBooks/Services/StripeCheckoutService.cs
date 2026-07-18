using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Options;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace FantasyBooks.Services;

public class StripeCheckoutService(
    LibraryContext db,
    IConfiguration configuration,
    ILogger<StripeCheckoutService> logger)
{
    public async Task<StripeCheckoutResult> CreateCartCheckoutAsync(
        IReadOnlyList<CartLine> lines,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var secretKey = StripeSecretResolver.ResolveSecretKey(configuration);
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return StripeCheckoutResult.Fail(
                    "Stripe is not configured. Set STRIPE_SECRET_KEY or Stripe__SecretKey on the host.");
            }

            if (!StripeSecretResolver.LooksLikeStripeSecret(secretKey))
            {
                return StripeCheckoutResult.Fail(
                    "Stripe secret key looks invalid (expected sk_test_… or sk_live_…). "
                    + $"Render currently has: {StripeSecretResolver.DescribeKeyPrefix(secretKey)}");
            }

            if (lines.Count == 0)
                return StripeCheckoutResult.Fail("Your treasury is empty.");

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            if (products.Count == 0)
            {
                return StripeCheckoutResult.Fail(
                    "No matching wares were found in the library. Remove stale lines from your satchel or refill the shop.");
            }

            if (lines.Any(l => !products.ContainsKey(l.ProductId)))
            {
                return StripeCheckoutResult.Fail(
                    "Some satchel lines no longer match the library (often after an import). Remove those rows on this page, then try checkout again.");
            }

            var optionGroupsByProduct = await db.ProductOptionGroups.AsNoTracking()
                .Where(g => productIds.Contains(g.ProductId))
                .Include(g => g.Choices)
                .ToListAsync(cancellationToken);

            var groupsLookup = optionGroupsByProduct
                .GroupBy(g => g.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<ProductOptionGroup>)g
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .Select(x =>
                        {
                            x.Choices = x.Choices.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
                            return x;
                        })
                        .ToList());

            foreach (var line in lines)
            {
                groupsLookup.TryGetValue(line.ProductId, out var groups);
                groups ??= [];
                var selections = ProductOptionsFormat.Normalize(line.SelectedOptions);
                if (!ProductOptionStore.SelectionsStillValid(groups, selections))
                {
                    var name = products.TryGetValue(line.ProductId, out var p) ? p.Name : "an item";
                    return StripeCheckoutResult.Fail(
                        $"Options for “{name}” are no longer valid. Remove that line from your satchel and add it again with current choices.");
                }
            }

            var lineItems = new List<SessionLineItemOptions>();
            var tiktokIds = new List<string>();

            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.ProductId, out var product))
                    continue;

                if (!string.IsNullOrWhiteSpace(product.TikTokId))
                    tiktokIds.Add(product.TikTokId);

                lineItems.Add(BuildLineItem(product, line.Quantity, line.SelectedOptions));
            }

            if (lineItems.Count == 0)
                return StripeCheckoutResult.Fail("Could not build a checkout session from the cart.");

            var idBlob = string.Join('|', tiktokIds.Distinct(StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(idBlob))
                idBlob = "none";

            return await CreateSessionAsync(
                secretKey,
                lineItems,
                successPath: "/Checkout/Success",
                cancelPath: "/Cart",
                metadata: new Dictionary<string, string>
                {
                    ["checkout_source"] = "cart",
                    ["tiktok_product_ids"] = TruncateMetadata(idBlob),
                },
                request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return FailFromException(ex, "cart");
        }
    }

    public async Task<StripeCheckoutResult> CreateBuyNowCheckoutAsync(
        int productId,
        HttpRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<CartOptionSelection>? selectedOptions = null)
    {
        try
        {
            var secretKey = StripeSecretResolver.ResolveSecretKey(configuration);
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                return StripeCheckoutResult.Fail(
                    "Stripe is not configured. Set STRIPE_SECRET_KEY or Stripe__SecretKey on the host.");
            }

            if (!StripeSecretResolver.LooksLikeStripeSecret(secretKey))
            {
                return StripeCheckoutResult.Fail(
                    "Stripe secret key looks invalid (expected sk_test_… or sk_live_…). "
                    + $"Render currently has: {StripeSecretResolver.DescribeKeyPrefix(secretKey)}");
            }

            if (productId <= 0)
                return StripeCheckoutResult.Fail("That title could not be found in the library.");

            var product = await db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product is null)
                return StripeCheckoutResult.Fail("That title could not be found in the library.");

            var groups = await ProductOptionStore.LoadForProductAsync(db, productId, cancellationToken);
            var selections = ProductOptionsFormat.Normalize(selectedOptions);
            if (!ProductOptionStore.SelectionsStillValid(groups, selections))
            {
                return StripeCheckoutResult.Fail(
                    groups.Count > 0
                        ? "Choose your options on the product page before buying."
                        : "That product’s options could not be verified. Try again from the product page.");
            }

            var tiktokId = string.IsNullOrWhiteSpace(product.TikTokId) ? "none" : product.TikTokId!;

            return await CreateSessionAsync(
                secretKey,
                new List<SessionLineItemOptions> { BuildLineItem(product, quantity: 1, selections) },
                successPath: "/Checkout/Success",
                cancelPath: "/Catalog",
                metadata: new Dictionary<string, string>
                {
                    ["checkout_source"] = "buy_now",
                    ["tiktok_product_ids"] = TruncateMetadata(tiktokId),
                },
                request,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return FailFromException(ex, "buy-now");
        }
    }

    private async Task<StripeCheckoutResult> CreateSessionAsync(
        string secretKey,
        List<SessionLineItemOptions> lineItems,
        string successPath,
        string cancelPath,
        Dictionary<string, string> metadata,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = PublicBaseUrl.Resolve(configuration, request);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogError("Invalid public base URL for Stripe checkout: {BaseUrl}", baseUrl);
            return StripeCheckoutResult.Fail(
                "Checkout is misconfigured (public site URL). Set App__PublicBaseUrl to your https://… Render URL.");
        }

        // Stripe requires https success/cancel URLs in live mode.
        if (secretKey.StartsWith("sk_live_", StringComparison.Ordinal)
            && !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return StripeCheckoutResult.Fail(
                $"Live Stripe keys require an https site URL (got {baseUrl}). Set App__PublicBaseUrl.");
        }

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = lineItems,
            SuccessUrl = $"{baseUrl}{successPath}?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}{cancelPath}",
            ShippingAddressCollection = new SessionShippingAddressCollectionOptions
            {
                AllowedCountries = new List<string> { "US", "CA", "GB", "AU", "NZ", "IE" },
            },
            CustomFields =
            [
                new SessionCustomFieldOptions
                {
                    Key = "order_notes",
                    Label = new SessionCustomFieldLabelOptions
                    {
                        Type = "custom",
                        Custom = "Special requests / order notes",
                    },
                    Type = "text",
                    Optional = true,
                },
            ],
            Metadata = metadata,
        };

        logger.LogInformation(
            "Creating Stripe checkout session. BaseUrl={BaseUrl}, LineItems={Count}, KeyMode={KeyMode}",
            baseUrl,
            lineItems.Count,
            secretKey.StartsWith("sk_live_", StringComparison.Ordinal) ? "live" : "test");

        var client = new StripeClient(secretKey.Trim());
        var service = new SessionService(client);
        var checkoutSession = await service.CreateAsync(options, cancellationToken: cancellationToken);
        if (string.IsNullOrEmpty(checkoutSession.Url))
            return StripeCheckoutResult.Fail("Stripe did not return a checkout URL.");

        return StripeCheckoutResult.Ok(checkoutSession.Url);
    }

    private StripeCheckoutResult FailFromException(Exception ex, string flow)
    {
        ex = Unwrap(ex);
        logger.LogError(ex, "Stripe checkout failed ({Flow}).", flow);

        if (ex is StripeException stripeEx)
            return StripeCheckoutResult.Fail($"Stripe: {stripeEx.Message}");

        if (ex is OperationCanceledException)
            return StripeCheckoutResult.Fail("Checkout timed out. Please try again.");

        if (ex is HttpRequestException httpEx)
            return StripeCheckoutResult.Fail($"Could not reach Stripe: {httpEx.Message}");

        return StripeCheckoutResult.Fail($"{ex.GetType().Name}: {ex.Message}");
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is AggregateException { InnerExceptions.Count: 1 } agg)
            ex = agg.InnerExceptions[0];

        if (ex.InnerException is StripeException stripeInner)
            return stripeInner;

        return ex;
    }

    private static SessionLineItemOptions BuildLineItem(
        Models.Product product,
        int quantity,
        IReadOnlyList<CartOptionSelection>? selectedOptions)
    {
        var unitCents = (long)Math.Round(product.Price * 100m, MidpointRounding.AwayFromZero);
        if (unitCents < 50)
            unitCents = 50;

        var baseName = string.IsNullOrWhiteSpace(product.Name) ? $"Item {product.Id}" : product.Name.Trim();
        var optionsSummary = ProductOptionsFormat.Summary(selectedOptions);
        var name = string.IsNullOrWhiteSpace(optionsSummary)
            ? baseName
            : $"{baseName} — {optionsSummary}";
        if (name.Length > 250)
            name = name[..247] + "…";

        var description = StripeDescription(product.Description, optionsSummary);

        var metadata = new Dictionary<string, string>
        {
            ["product_id"] = product.Id.ToString(),
        };
        foreach (var opt in ProductOptionsFormat.Normalize(selectedOptions))
        {
            var key = SanitizeMetadataKey("option_" + opt.GroupName);
            if (metadata.Count >= 45)
                break;
            if (!metadata.ContainsKey(key))
                metadata[key] = TruncateMetadata(opt.ChoiceLabel);
        }

        return new SessionLineItemOptions
        {
            Quantity = quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "gbp",
                UnitAmount = unitCents,
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = name,
                    Description = description,
                    Metadata = metadata,
                },
            },
        };
    }

    private static string SanitizeMetadataKey(string key)
    {
        var chars = key.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .Take(40)
            .ToArray();
        var s = new string(chars);
        return string.IsNullOrEmpty(s) ? "option" : s;
    }

    private static string? StripeDescription(string? htmlDescription, string optionsSummary)
    {
        var plain = HtmlPlainText.FromHtml(htmlDescription)?.Replace('\n', ' ').Trim();
        string? text;
        if (!string.IsNullOrWhiteSpace(optionsSummary) && !string.IsNullOrWhiteSpace(plain))
            text = $"{optionsSummary}. {plain}";
        else if (!string.IsNullOrWhiteSpace(optionsSummary))
            text = optionsSummary;
        else
            text = plain;

        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text.Length <= 500 ? text : text[..497] + "…";
    }

    private static string TruncateMetadata(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "none";
        return value.Length <= 500 ? value : value[..500];
    }
}

public sealed record StripeCheckoutResult(bool Succeeded, string? RedirectUrl, string? ErrorMessage)
{
    public static StripeCheckoutResult Ok(string redirectUrl) => new(true, redirectUrl, null);
    public static StripeCheckoutResult Fail(string errorMessage) => new(false, null, errorMessage);
}
