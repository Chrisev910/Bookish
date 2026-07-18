using System.Text.Json;
using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasyBooks.Services;

public sealed class TikTokFeedSyncService(
    LibraryContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<TikTokFeedOptions> options,
    ILogger<TikTokFeedSyncService> logger)
{
    public const string HttpClientName = "TikTokFeedSync";

    public async Task<TikTokFeedSyncResult> SyncLatestAsync(
        string? usernameOverride = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var username = NormalizeUsername(usernameOverride ?? opts.Username);
        if (string.IsNullOrWhiteSpace(username))
            return TikTokFeedSyncResult.Fail("Set a TikTok username (handle without @).");

        if (string.IsNullOrWhiteSpace(opts.RapidApiKey))
            return TikTokFeedSyncResult.Fail(
                "Missing RapidAPI key. In Render → Environment, set key name exactly to TikTokFeed__RapidApiKey (two underscores), paste the key as the value, then Manual Deploy → Deploy latest.");

        var take = Math.Clamp(opts.TakeCount <= 0 ? 4 : opts.TakeCount, 1, 12);
        var host = string.IsNullOrWhiteSpace(opts.RapidApiHost)
            ? "tiktok-scraper7.p.rapidapi.com"
            : opts.RapidApiHost.Trim();

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url =
            $"https://{host}/user/posts?unique_id={Uri.EscapeDataString(username)}&count={take}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("x-rapidapi-key", opts.RapidApiKey.Trim());
        request.Headers.TryAddWithoutValidation("x-rapidapi-host", host);

        string body;
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TikTok feed sync HTTP {Status}: {Body}",
                    (int)response.StatusCode,
                    Truncate(body, 300));
                return TikTokFeedSyncResult.Fail(
                    $"Feed API returned HTTP {(int)response.StatusCode}. Check your RapidAPI subscription and key.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "TikTok feed sync request failed");
            return TikTokFeedSyncResult.Fail($"Could not reach the feed API: {ex.Message}");
        }

        var videoUrls = ParseVideoUrls(body, username);
        if (videoUrls.Count == 0)
        {
            logger.LogWarning("TikTok feed sync parsed 0 videos. Body: {Body}", Truncate(body, 500));
            return TikTokFeedSyncResult.Fail(
                "No videos found for that account (private account, wrong username, or unexpected API response).");
        }

        // Replace feed with latest posts so the footer stays current.
        var existing = await db.TikTokVideos.ToListAsync(cancellationToken);
        db.TikTokVideos.RemoveRange(existing);

        var now = DateTime.UtcNow;
        // Preserve newest-first ordering via DateCreated offsets.
        for (var i = 0; i < videoUrls.Count; i++)
        {
            db.TikTokVideos.Add(new TikTokVideo
            {
                VideoUrl = videoUrls[i],
                IsActive = true,
                DateCreated = now.AddSeconds(-i),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "TikTok feed synced {Count} videos for @{Username}",
            videoUrls.Count,
            username);

        return new TikTokFeedSyncResult(true, videoUrls.Count, username, null);
    }

    internal static List<string> ParseVideoUrls(string json, string username)
    {
        var results = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Common shapes: { data: { videos: [...] } } or { data: [...] }
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("videos", out var videos)
                && videos.ValueKind == JsonValueKind.Array)
            {
                CollectFromArray(videos, username, results);
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                CollectFromArray(data, username, results);
            }
            else if (data.ValueKind == JsonValueKind.Object
                     && data.TryGetProperty("aweme_list", out var aweme)
                     && aweme.ValueKind == JsonValueKind.Array)
            {
                CollectFromArray(aweme, username, results);
            }
        }

        if (results.Count == 0
            && root.TryGetProperty("videos", out var topVideos)
            && topVideos.ValueKind == JsonValueKind.Array)
        {
            CollectFromArray(topVideos, username, results);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectFromArray(JsonElement array, string username, List<string> results)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var url = TryReadUrl(item, username);
            if (!string.IsNullOrWhiteSpace(url))
                results.Add(url);
        }
    }

    private static string? TryReadUrl(JsonElement item, string username)
    {
        foreach (var key in new[] { "share_url", "shareUrl", "video_url", "videoUrl", "url", "web_video_url" })
        {
            if (item.TryGetProperty(key, out var prop)
                && prop.ValueKind == JsonValueKind.String
                && IsTikTokVideoUrl(prop.GetString()))
            {
                return prop.GetString()!.Trim();
            }
        }

        string? id = null;
        foreach (var key in new[] { "video_id", "videoId", "aweme_id", "awemeId", "id" })
        {
            if (!item.TryGetProperty(key, out var prop))
                continue;

            id = prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(id) && id.All(char.IsDigit) && id.Length >= 5)
                break;
            id = null;
        }

        if (string.IsNullOrWhiteSpace(id))
            return null;

        return $"https://www.tiktok.com/@{username}/video/{id}";
    }

    private static bool IsTikTokVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return uri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUsername(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim();
        if (s.StartsWith('@'))
            s = s[1..];
        // Allow pasting a profile URL
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && uri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(segment))
                s = segment.StartsWith('@') ? segment[1..] : segment;
        }

        return s.Trim();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

public sealed record TikTokFeedSyncResult(bool Ok, int Imported, string? Username, string? Error)
{
    public static TikTokFeedSyncResult Fail(string error) => new(false, 0, null, error);

    public string FlashMessage => Ok
        ? $"Synced {Imported} latest video(s) from @{Username}."
        : Error ?? "Sync failed.";
}
