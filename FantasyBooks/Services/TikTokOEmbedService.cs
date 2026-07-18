using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace FantasyBooks.Services;

/// <summary>Fetches TikTok oEmbed HTML with memory caching. Failures return null (never throw).</summary>
public sealed class TikTokOEmbedService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<TikTokOEmbedService> logger)
{
    public const string HttpClientName = "TikTokOEmbed";

    public async Task<string?> GetEmbedHtmlAsync(string? videoUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            return null;

        var url = videoUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = "tiktok-oembed:" + url;
        if (cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var endpoint = "https://www.tiktok.com/oembed?url=" + Uri.EscapeDataString(url);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TikTok oEmbed failed ({Status}) for {Url}", (int)response.StatusCode, url);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("html", out var htmlProp))
                return null;

            var html = htmlProp.GetString();
            if (string.IsNullOrWhiteSpace(html))
                return null;

            cache.Set(key, html, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
            });
            return html;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TikTok oEmbed error for {Url}", url);
            return null;
        }
    }
}
