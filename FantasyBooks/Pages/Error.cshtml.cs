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

    public string? ErrorDetail { get; set; }

    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(ILogger<ErrorModel> logger)
    {
        _logger = logger;
    }

    public IActionResult OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var ex = Unwrap(feature?.Error);

        if (ex is not null)
            _logger.LogError(ex, "Unhandled exception for {Path}", feature?.Path);

        // Avoid TempData here — session is often unavailable on the exception-handler re-execute.
        if (ex is AntiforgeryValidationException
            || (ex?.GetType().Name.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var path = feature?.Path ?? string.Empty;
            if (path.Contains("/Admin", StringComparison.OrdinalIgnoreCase))
                return RedirectToPage("/Admin/Login");

            if (path.Contains("BuyNow", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/Catalog", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToPage("/Catalog", new { error = "Your checkout form expired. Please try again." });
            }

            return RedirectToPage("/Cart", new { error = "Your checkout form expired. Please try again." });
        }

        if (ex is not null)
        {
            var root = ex;
            while (root.InnerException is not null)
                root = root.InnerException;
            ErrorDetail = $"{ex.GetType().Name}: {ex.Message}"
                + (root != ex ? $" | {root.GetType().Name}: {root.Message}" : "");
            if (!string.IsNullOrEmpty(feature?.Path))
                ErrorDetail += $" (at {feature.Path})";
        }

        return Page();
    }

    private static Exception? Unwrap(Exception? ex)
    {
        while (ex is AggregateException { InnerExceptions.Count: 1 } agg)
            ex = agg.InnerExceptions[0];
        while (ex?.InnerException is not null
               && ex is not AntiforgeryValidationException
               && ex.GetType().Name.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase) == false)
        {
            // Prefer the innermost useful message for display, but keep antiforgery at the top if present.
            if (ex.InnerException is AntiforgeryValidationException innerAf)
                return innerAf;
            break;
        }

        return ex;
    }
}
