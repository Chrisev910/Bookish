using System.Globalization;
using System.Text.Json;
using FantasyBooks.Data;
using FantasyBooks.Models;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Services;

public class TikTokIntegrationService(LibraryContext db)
{
    private static readonly string[] NameKeys = ["Product Name", "product_name", "ProductName", "Title", "title", "Product Title"];
    private static readonly string[] ImageKeys = ["Main Image", "main_image", "MainImage", "Image", "image", "Main image URL", "Product Image"];
    private static readonly string[] TikTokIdKeys =
    [
        "TikTokId", "tiktok_id", "TikTok Product ID", "TikTok product id", "Product ID", "product_id", "ProductID", "Product Id", "SKU", "sku",
    ];
    private static readonly string[] SalePriceKeys =
    [
        "Sale Price", "sale_price", "SalePrice", "Price", "price", "Selling Price", "Your Price", "List Price", "Current Price",
    ];
    private static readonly string[] DescriptionKeys =
    [
        "Description", "description", "Product Description", "Detail", "Body", "Long Description",
    ];

    /// <summary>Auto-detect JSON vs CSV (TikTok Seller Center export).</summary>
    public async Task<TikTokSyncResult> SyncMagicPasteAsync(string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return TikTokSyncResult.Fail("Nothing to import.");

        var trimmed = payload.TrimStart();
        if (trimmed.Length == 0)
            return TikTokSyncResult.Fail("Nothing to import.");

        var first = trimmed[0];
        if (first is '[' or '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var rows = ParseJsonRows(doc.RootElement);
                return await PersistRowsAsync(rows, requireTikTokId: true, cancellationToken);
            }
            catch (JsonException ex)
            {
                return TikTokSyncResult.Fail($"Invalid JSON: {ex.Message}");
            }
        }

        try
        {
            var rows = ParseCsvRows(payload);
            return await PersistRowsAsync(rows, requireTikTokId: true, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return TikTokSyncResult.Fail(ex.Message);
        }
    }

    public async Task<TikTokSyncResult> SyncFromJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            return TikTokSyncResult.Fail("Payload is empty.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var rows = ParseJsonRows(doc.RootElement);
            return await PersistRowsAsync(rows, requireTikTokId: false, cancellationToken);
        }
        catch (JsonException ex)
        {
            return TikTokSyncResult.Fail($"Invalid JSON: {ex.Message}");
        }
    }

    public async Task<TikTokSyncResult> SyncFromCsvAsync(string csv, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return TikTokSyncResult.Fail("CSV is empty.");

        try
        {
            var rows = ParseCsvRows(csv);
            return await PersistRowsAsync(rows, requireTikTokId: false, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return TikTokSyncResult.Fail(ex.Message);
        }
    }

    private static List<TikTokImportRow> ParseJsonRows(JsonElement root)
    {
        var list = new List<TikTokImportRow>();

        foreach (var obj in EnumerateJsonObjects(root))
        {
            var name = GetFirstStringProperty(obj, NameKeys);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var image = GetFirstStringProperty(obj, ImageKeys);
            var id = GetFirstStringProperty(obj, TikTokIdKeys);
            var desc = GetFirstStringProperty(obj, DescriptionKeys);
            var priceRaw = GetFirstStringProperty(obj, SalePriceKeys);
            var price = ParseMoney(priceRaw);

            list.Add(new TikTokImportRow(
                TikTokId: string.IsNullOrWhiteSpace(id) ? null : id.Trim(),
                Name: name.Trim(),
                ImageUrl: string.IsNullOrWhiteSpace(image) ? null : image.Trim(),
                Description: string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
                Price: price));
        }

        return list;
    }

    private static IEnumerable<JsonElement> EnumerateJsonObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object)
                    yield return el;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                        yield return el;
                }
            }
        }
    }

    private static string? GetFirstStringProperty(JsonElement obj, IEnumerable<string> candidateNames)
    {
        var set = new HashSet<string>(candidateNames, StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
        {
            if (!set.Contains(prop.Name))
                continue;

            return prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString(),
            };
        }

        return null;
    }

    private static List<TikTokImportRow> ParseCsvRows(string csv)
    {
        var lines = SplitCsvPhysicalLines(csv);
        if (lines.Count == 0)
            throw new InvalidOperationException("CSV has no rows.");

        var headerFields = SplitCsvLine(lines[0]);
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerFields.Count; i++)
        {
            var key = headerFields[i].Trim().Trim('"').TrimStart('\uFEFF');
            headerIndex[key] = i;
        }

        static int? FindColumn(Dictionary<string, int> h, params string[] names)
        {
            foreach (var n in names)
            {
                if (h.TryGetValue(n, out var idx))
                    return idx;
            }

            return null;
        }

        var nameCol = FindColumn(headerIndex, "Product Name", "ProductName", "Title");
        var imageCol = FindColumn(headerIndex, "Main Image", "MainImage", "Image", "Product Image");
        if (nameCol is null)
            throw new InvalidOperationException("CSV must include a 'Product Name' column (or Title / ProductName).");

        var idCol = FindColumn(headerIndex, "TikTokId", "TikTok Product ID", "Product ID", "ProductID", "SKU");
        var priceCol = FindColumn(headerIndex, "Sale Price", "SalePrice", "Price", "Selling Price", "Your Price");
        var descCol = FindColumn(headerIndex, "Description", "Product Description", "Detail");

        var rows = new List<TikTokImportRow>();
        for (var li = 1; li < lines.Count; li++)
        {
            var fields = SplitCsvLine(lines[li]);
            string Cell(int? col)
            {
                if (col is null || col.Value >= fields.Count)
                    return "";
                return fields[col.Value].Trim();
            }

            var name = Cell(nameCol);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var image = imageCol is null ? "" : Cell(imageCol);
            var id = Cell(idCol);
            var price = ParseMoney(Cell(priceCol));
            var desc = Cell(descCol);

            rows.Add(new TikTokImportRow(
                TikTokId: string.IsNullOrWhiteSpace(id) ? null : id,
                Name: name,
                ImageUrl: string.IsNullOrWhiteSpace(image) ? null : image,
                Description: string.IsNullOrWhiteSpace(desc) ? null : desc,
                Price: price));
        }

        return rows;
    }

    private static List<string> SplitCsvPhysicalLines(string text)
    {
        var lines = new List<string>();
        var span = text.AsSpan();
        var start = 0;
        var inQuotes = false;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < span.Length && span[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && (c == '\n' || c == '\r'))
            {
                var line = text[start..i].TrimEnd('\r', '\n');
                if (line.Length > 0)
                    lines.Add(line);
                if (c == '\r' && i + 1 < span.Length && span[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
        }

        var tail = text[start..].TrimEnd('\r', '\n');
        if (tail.Length > 0)
            lines.Add(tail);

        return lines;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        fields.Add(sb.ToString());
        return fields;
    }

    private static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        foreach (var ch in new[] { '$', '€', '£', '¥', ',' })
            s = s.Replace(ch.ToString(), "", StringComparison.Ordinal);

        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
            return d;

        return null;
    }

    private async Task<TikTokSyncResult> PersistRowsAsync(
        IReadOnlyList<TikTokImportRow> rows,
        bool requireTikTokId,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return TikTokSyncResult.Fail("No product rows were found in the payload.");

        var messages = new List<string>();
        var skipped = 0;

        var deduped = rows
            .GroupBy(
                r => string.IsNullOrWhiteSpace(r.TikTokId) ? $"n:{r.Name}" : $"id:{r.TikTokId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var created = 0;
        var updated = 0;

        foreach (var row in deduped)
        {
            if (string.IsNullOrWhiteSpace(row.TikTokId))
            {
                if (requireTikTokId)
                {
                    skipped++;
                    messages.Add($"Skipped (no TikTok id): {row.Name}");
                    continue;
                }
            }

            Product? existing = null;
            if (!string.IsNullOrWhiteSpace(row.TikTokId))
            {
                existing = await db.Products.FirstOrDefaultAsync(
                    p => p.TikTokId == row.TikTokId,
                    cancellationToken);
            }

            existing ??= await db.Products.FirstOrDefaultAsync(p => p.Name == row.Name, cancellationToken);

            if (existing is null)
            {
                var p = new Product
                {
                    TikTokId = row.TikTokId,
                    Name = row.Name,
                    ImageUrl = row.ImageUrl,
                    Description = row.Description,
                    Price = row.Price ?? 0,
                };
                db.Products.Add(p);
                created++;
            }
            else
            {
                existing.Name = row.Name;
                existing.ImageUrl = row.ImageUrl;
                if (row.Description is not null)
                    existing.Description = row.Description;
                if (row.Price is not null)
                    existing.Price = row.Price.Value;

                if (!string.IsNullOrWhiteSpace(row.TikTokId))
                    existing.TikTokId = row.TikTokId;

                updated++;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            messages.Add(ex.Message);
            return new TikTokSyncResult(0, 0, skipped, messages, Ok: false);
        }

        return new TikTokSyncResult(created, updated, skipped, messages, Ok: true);
    }

    private sealed record TikTokImportRow(
        string? TikTokId,
        string Name,
        string? ImageUrl,
        string? Description,
        decimal? Price);
}

public sealed record TikTokSyncResult(int Created, int Updated, int Skipped, IReadOnlyList<string> Messages, bool Ok)
{
    public static TikTokSyncResult Fail(string message) =>
        new(0, 0, 0, [message], false);
}
