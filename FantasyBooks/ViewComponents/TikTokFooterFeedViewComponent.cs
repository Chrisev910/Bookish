using System.Text.Json;
using FantasyBooks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FantasyBooks.ViewComponents;

public class TikTokFooterFeedViewComponent(
    LibraryContext db,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<TikTokFooterFeedViewComponent> logger) : ViewComponent
{
    public const string HttpClientName = "TikTokOEmbed";

    public async Task<IViewComponentResult> InvokeAsync()
    {
        List<string> urls;
        try
        {
            urls = await db.TikTokVideos.AsNoTracking()
                .Where(v => v.IsActive)
                .OrderByDescending(v => v.DateCreated)
                .Take(4)
                .Select(v => v.VideoUrl)
                .ToListAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok footer feed query failed");
            return Content(string.Empty);
        }

        if (urls.Count == 0)
            return Content(string.Empty);

        var client = httpClientFactory.CreateClient(HttpClientName);
        var tasks = urls.Select(url => GetOEmbedHtmlAsync(client, url, HttpContext.RequestAborted));
        var results = await Task.WhenAll(tasks);

        var embeds = new List<TikTokEmbedItem>(results.Length);
        for (var i = 0; i < results.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(results[i]))
                embeds.Add(new TikTokEmbedItem(urls[i], results[i]!));
        }

        if (embeds.Count == 0)
            return Content(string.Empty);

        return View(embeds);
    }

    private async Task<string?> GetOEmbedHtmlAsync(HttpClient client, string videoUrl, CancellationToken cancellationToken)
    {
        var key = "tiktok-oembed:" + videoUrl;
        if (cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        try
        {
            var endpoint = "https://www.tiktok.com/oembed?url=" + Uri.EscapeDataString(videoUrl);
            using var response = await client.GetAsync(endpoint, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TikTok oEmbed failed ({Status}) for {Url}", (int)response.StatusCode, videoUrl);
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
            logger.LogWarning(ex, "TikTok oEmbed error for {Url}", videoUrl);
            return null;
        }
    }
}

public sealed record TikTokEmbedItem(string VideoUrl, string EmbedHtml);
