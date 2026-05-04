namespace FantasyBooks;

/// <summary>Resolves the public site URL for Stripe return links (Render/proxy needs a configured absolute URL when <c>Request.Scheme</c> is wrong).</summary>
public static class PublicBaseUrl
{
    public static string Resolve(IConfiguration configuration, HttpRequest request)
    {
        var configured = configuration["App:PublicBaseUrl"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        return $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');
    }
}
