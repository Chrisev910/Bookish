using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FantasyBooks.Data;

/// <summary>
/// SQLite/Turso databases created before newer columns existed are not upgraded by <c>EnsureCreated()</c>.
/// Applies additive columns with best-effort ALTER TABLE (safe if column already exists).
/// </summary>
public static class LibrarySchemaPatch
{
    private static readonly string[] LegacyColumnsToDrop =
    [
        "Category",
        "StockQuantity",
        "Weight",
        "IsBundle",
        "BundleItems",
        "Rarity",
        "TikTokProductId",
    ];

    public static async Task ApplyAsync(
        LibraryContext db,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        await TryExecAsync(db, """ALTER TABLE "Products" ADD COLUMN "ImageUrl" TEXT NULL;""", logger, cancellationToken);
        await TryExecAsync(db, """ALTER TABLE "Products" ADD COLUMN "TikTokId" TEXT NULL;""", logger, cancellationToken);
        await TryExecAsync(db, """ALTER TABLE "Products" ADD COLUMN "ImageContentType" TEXT NULL;""", logger, cancellationToken);
        await TryExecAsync(db, """ALTER TABLE "Products" ADD COLUMN "ImageData" BLOB NULL;""", logger, cancellationToken);

        await TryExecAsync(
            db,
            """
            UPDATE "Products" SET "TikTokId" = "TikTokProductId"
            WHERE ("TikTokId" IS NULL OR TRIM("TikTokId") = '')
              AND "TikTokProductId" IS NOT NULL AND TRIM("TikTokProductId") != '';
            """,
            logger,
            cancellationToken);

        foreach (var col in LegacyColumnsToDrop)
        {
            await TryExecAsync(
                db,
                $"""ALTER TABLE "Products" DROP COLUMN "{col}";""",
                logger,
                cancellationToken);
        }
    }

    private static async Task TryExecAsync(
        LibraryContext db,
        string sql,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (Exception ex)
        {
            // Duplicate column / missing legacy column / Turso DDL quirks — never fail startup.
            logger?.LogDebug(ex, "Schema patch statement skipped: {Sql}", sql.Trim());
        }
    }
}
