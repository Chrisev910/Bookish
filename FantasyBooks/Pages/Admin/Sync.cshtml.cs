using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyBooks.Pages.Admin;

public class SyncModel : PageModel
{
    private readonly TikTokIntegrationService _tikTok;

    public SyncModel(TikTokIntegrationService tikTok)
    {
        _tikTok = tikTok;
    }

    [BindProperty]
    public string Payload { get; set; } = "";

    [BindProperty]
    public SyncPayloadKind Kind { get; set; } = SyncPayloadKind.Json;

    [BindProperty]
    public IFormFile? CsvUpload { get; set; }

    public TikTokSyncResult? LastResult { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var body = Payload ?? "";
        if (Kind == SyncPayloadKind.Csv && CsvUpload is { Length: > 0 })
        {
            await using var ms = new MemoryStream();
            await CsvUpload.CopyToAsync(ms, cancellationToken);
            body = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        LastResult = Kind switch
        {
            SyncPayloadKind.Json => await _tikTok.SyncFromJsonAsync(body, cancellationToken),
            SyncPayloadKind.Csv => await _tikTok.SyncFromCsvAsync(body, cancellationToken),
            _ => TikTokSyncResult.Fail("Unknown format."),
        };

        return Page();
    }
}

public enum SyncPayloadKind
{
    Json = 0,
    Csv = 1,
}
