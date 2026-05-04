using System.Globalization;
using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;

namespace FantasyBooks.Pages.Admin;

public class ImportModel : PageModel
{
    private readonly TikTokIntegrationService _tikTok;
    private readonly LibraryContext _db;

    public ImportModel(TikTokIntegrationService tikTok, LibraryContext db)
    {
        _tikTok = tikTok;
        _db = db;
    }

    [BindProperty]
    public string MagicPaste { get; set; } = "";

    [BindProperty]
    public IFormFile? TikTokWorkbook { get; set; }

    public TikTokSyncResult? LastResult { get; private set; }

    public string? FlashMessage { get; set; }

    public void OnGet()
    {
        FlashMessage = TempData["FlashMessage"] as string;
    }

    public async Task<IActionResult> OnPostCastSyncSpellAsync(CancellationToken cancellationToken)
    {
        LastResult = await _tikTok.SyncMagicPasteAsync(MagicPaste ?? "", cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
    {
        if (TikTokWorkbook is not { Length: > 0 })
        {
            TempData["FlashMessage"] = "Please choose an Excel file (.xlsx) from TikTok.";
            return RedirectToPage();
        }

        var ext = Path.GetExtension(TikTokWorkbook.FileName);
        if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["FlashMessage"] = "Only .xlsx files are supported.";
            return RedirectToPage();
        }

        await using var uploadStream = TikTokWorkbook.OpenReadStream();
        await using var ms = new MemoryStream();
        await uploadStream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        List<IDictionary<string, object>> rows;
        try
        {
            rows = ReadRowsWithTikTokHeaders(ms);
        }
        catch (Exception ex)
        {
            TempData["FlashMessage"] = $"Could not read that workbook: {ex.Message}";
            return RedirectToPage();
        }

        var upserted = 0;

        foreach (var row in rows)
        {
            var tikTokId = FirstCell(row, "product_id", "Product ID", "ProductID");
            if (string.IsNullOrWhiteSpace(tikTokId))
                tikTokId = FirstCell(row, "sku_id", "SKU ID");

            if (string.IsNullOrWhiteSpace(tikTokId) || !LooksLikeTikTokId(tikTokId))
                continue;

            tikTokId = tikTokId.Trim();

            var name = FirstCell(row, "product_name", "Product name", "Product Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var price = ParseMoney(FirstCell(row, "price", "Retail Price (Local Currency)", "Retail Price", "Sale Price")) ?? 0m;
            var imageUrl = NullIfEmpty(FirstCell(row, "main_image", "Main image", "Main Image", "Main Image URL"));
            var description = NullIfEmpty(FirstCell(row, "product_description", "Product description", "Product Description"));

            var existing = await _db.Products.FirstOrDefaultAsync(p => p.TikTokId == tikTokId, cancellationToken);
            existing ??= _db.Products.Local.FirstOrDefault(p => p.TikTokId == tikTokId);

            if (existing is null)
            {
                _db.Products.Add(new Product
                {
                    TikTokId = tikTokId,
                    Name = name.Trim(),
                    Price = price,
                    ImageUrl = imageUrl,
                    Description = description,
                });
            }
            else
            {
                existing.Name = name.Trim();
                existing.Price = price;
                existing.ImageUrl = imageUrl;
                existing.Description = description;
                existing.TikTokId = tikTokId;
            }

            upserted++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        TempData["FlashMessage"] = upserted == 0
            ? "No product rows were imported. Open the TikTok Template sheet, add rows below the header block (row 6+), and ensure Product ID / product_id and Product name are filled."
            : $"Import complete: {upserted} product row(s) saved. ";

        return RedirectToPage("/Catalog");
    }

    public async Task<IActionResult> OnPostClearAllInventoryAsync(CancellationToken cancellationToken)
    {
        await _db.Products.ExecuteDeleteAsync(cancellationToken);
        FlashMessage = "The ledger lies empty — every listing has been struck from the rolls.";
        return Page();
    }

    public async Task<IActionResult> OnPostResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await _db.Database.EnsureDeletedAsync(cancellationToken);
        await _db.Database.EnsureCreatedAsync(cancellationToken);
        await LibrarySchemaPatch.ApplyAsync(_db, cancellationToken);
        SeedData.Initialize(_db);

        TempData["FlashMessage"] = "Database reset complete. Schema recreated; catalog is empty until you import again.";
        return RedirectToPage();
    }

    /// <summary>TikTok workbooks use a "Template" sheet; row 1 keys are often internal (product_id, price, …).</summary>
    private static List<IDictionary<string, object>> ReadRowsWithTikTokHeaders(MemoryStream ms)
    {
        static List<IDictionary<string, object>> Query(MemoryStream s, string? sheetName)
        {
            s.Position = 0;
            var q = string.IsNullOrEmpty(sheetName)
                ? s.Query(useHeaderRow: true, excelType: ExcelType.XLSX)
                : s.Query(useHeaderRow: true, sheetName: sheetName, excelType: ExcelType.XLSX);
            return q.Cast<IDictionary<string, object>>().ToList();
        }

        List<IDictionary<string, object>> rows;
        try
        {
            rows = Query(ms, "Template");
        }
        catch
        {
            rows = [];
        }

        if (rows.Count == 0)
        {
            try
            {
                rows = Query(ms, null);
            }
            catch
            {
                rows = [];
            }
        }

        return rows;
    }

    private static string? FirstCell(IDictionary<string, object> row, params string[] headerAliases)
    {
        foreach (var alias in headerAliases)
        {
            foreach (var kv in row)
            {
                if (KeysMatch(kv.Key, alias))
                    return CellToString(kv.Value)?.Trim();
            }
        }

        return null;
    }

    private static bool KeysMatch(string keyInRow, string wanted) =>
        string.Equals(NormalizeKey(keyInRow), NormalizeKey(wanted), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKey(string key) =>
        key.Trim().TrimStart('\uFEFF');

    /// <summary>Skip instruction / legend rows (e.g. "Mandatory", "V3") that are not real TikTok ids.</summary>
    private static bool LooksLikeTikTokId(string raw)
    {
        var s = raw.Trim();
        if (s.Length < 8)
            return false;

        foreach (var bad in HeaderNoiseValues)
        {
            if (string.Equals(s, bad, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // TikTok product ids in exports are long numeric strings
        return s.All(char.IsDigit);
    }

    private static readonly string[] HeaderNoiseValues =
    [
        "product_id", "sku_id", "Product ID", "SKU ID", "Mandatory", "Optional", "Uneditable",
        "V3", "metric", "category", "All_Information",
    ];

    private static string? CellToString(object? value) =>
        value switch
        {
            null => null,
            string str => str,
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        foreach (var ch in new[] { '$', '€', '£', '¥' })
            s = s.Replace(ch.ToString(), "", StringComparison.Ordinal);
        s = s.Replace(",", "", StringComparison.Ordinal);

        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d) ? d : null;
    }
}
