using System.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyBooks.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(ILogger<ErrorModel> logger)
    {
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is { } ex)
            _logger.LogError(ex, "Unhandled exception for {Path}", feature.Path);

        // Checkout POSTs fail with a generic error page when antiforgery cookies break behind a proxy
        // or after a recycle. Send shoppers back with a retry message instead.
        if (feature?.Error is AntiforgeryValidationException)
        {
            var path = feature.Path ?? string.Empty;
            if (path.Contains("BuyNow", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/Checkout/", StringComparison.OrdinalIgnoreCase))
            {
                TempData["FlashMessage"] = "Your checkout form expired. Please try again.";
                return RedirectToPage("/Catalog");
            }

            TempData["CartError"] = "Your checkout form expired. Please try again.";
            return RedirectToPage("/Cart");
        }

        return Page();
    }
}
