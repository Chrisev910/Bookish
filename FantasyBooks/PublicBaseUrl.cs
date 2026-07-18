namespace FantasyBooks;

/// <summary>Resolves the public site URL for Stripe return links (Render/proxy needs a configured absolute URL when <c>Request.Scheme</c> is wrong).</summary>
public static class PublicBaseUrl
{
    public static string Resolve(IConfiguration configuration, HttpRequest request)
    {
        var configured = configuration["App:PublicBaseUrl"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        var renderUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")?.Trim();
        if (!string.IsNullOrWhiteSpace(renderUrl))
            return renderUrl.TrimEnd('/');

        var proto = FirstForwardedValue(request.Headers["X-Forwarded-Proto"])
            ?? request.Scheme;
        var host = FirstForwardedValue(request.Headers["X-Forwarded-Host"])
            ?? request.Host.Value;

        return $"{proto}://{host}".TrimEnd('/');
    }

    private static string? FirstForwardedValue(Microsoft.Extensions.Primitives.StringValues values)
    {
        var raw = values.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Split(',', 2)[0].Trim();
    }
}
