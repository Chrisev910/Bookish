using System.ComponentModel.DataAnnotations;
using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Options;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FantasyBooks.Pages.Admin.TikTok;

public class IndexModel(
    LibraryContext db,
    LibraryDatabaseInfo dbInfo,
    TikTokFeedSyncService feedSync,
    IOptions<TikTokFeedOptions> feedOptions) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public SyncInputModel SyncInput { get; set; } = new();

    public List<TikTokVideo> Videos { get; private set; } = [];

    public string? FlashMessage { get; set; }

    public string DatabaseDescription => dbInfo.Description;

    public bool FeedApiConfigured => !string.IsNullOrWhiteSpace(feedOptions.Value.RapidApiKey);

    public string ConfiguredUsername => feedOptions.Value.Username?.Trim().TrimStart('@') ?? "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        FlashMessage = TempData["FlashMessage"] as string;
        ViewData["LibraryDatabase"] = dbInfo.Description;
        SyncInput.Username = string.IsNullOrWhiteSpace(ConfiguredUsername) ? "" : ConfiguredUsername;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        // Clear sync-only noise; this handler only cares about Input.*
        ModelState.Remove(nameof(SyncInput));
        ModelState.Remove("SyncInput.Username");

        var url = Input.VideoUrl?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            ModelState.AddModelError("Input.VideoUrl", "Paste a TikTok video URL.");
            SyncInput.Username = ConfiguredUsername;
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.VideoUrl", "Paste a full TikTok video URL (tiktok.com).");
            SyncInput.Username = ConfiguredUsername;
            await LoadAsync(cancellationToken);
            return Page();
        }

        db.TikTokVideos.Add(new TikTokVideo
        {
            VideoUrl = url,
            IsActive = Input.IsActive,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["FlashMessage"] = "TikTok video saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncFeedAsync(CancellationToken cancellationToken)
    {
        var result = await feedSync.SyncLatestAsync(SyncInput.Username, cancellationToken);
        TempData["FlashMessage"] = result.FlashMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.TikTokVideos.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (row is not null)
        {
            row.IsActive = !row.IsActive;
            await db.SaveChangesAsync(cancellationToken);
            TempData["FlashMessage"] = row.IsActive ? "Video is now live in the footer." : "Video hidden from the footer.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.TikTokVideos.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (row is not null)
        {
            db.TikTokVideos.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            TempData["FlashMessage"] = "TikTok video removed.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Videos = await db.TikTokVideos
                .AsNoTracking()
                .OrderByDescending(v => v.DateCreated)
                .ToListAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Corrupt/legacy date TEXT should not brick the admin page.
            Videos = [];
            FlashMessage ??= "Could not load saved videos (bad date format). Click Sync latest videos to rebuild the feed.";
        }
    }

    public class InputModel
    {
        [Display(Name = "TikTok video URL")]
        [StringLength(2000)]
        public string VideoUrl { get; set; } = "";

        [Display(Name = "Show in footer")]
        public bool IsActive { get; set; } = true;
    }

    public class SyncInputModel
    {
        [Display(Name = "TikTok username")]
        [StringLength(100)]
        public string Username { get; set; } = "";
    }
}
