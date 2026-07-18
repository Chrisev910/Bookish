using FantasyBooks.Data;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.ViewComponents;

public class TikTokFooterFeedViewComponent(
    LibraryContext db,
    TikTokOEmbedService oEmbed,
    ILogger<TikTokFooterFeedViewComponent> logger) : ViewComponent
{
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

        var tasks = urls.Select(url => oEmbed.GetEmbedHtmlAsync(url, HttpContext.RequestAborted));
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
}

public sealed record TikTokEmbedItem(string VideoUrl, string EmbedHtml);
