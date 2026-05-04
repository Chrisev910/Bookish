using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Data;

/// <summary>
/// SQLite databases created before newer columns existed are not upgraded by <c>EnsureCreated()</c>.
/// Applies additive columns, one-time backfills, then drops legacy columns removed from the model.
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

    public static async Task ApplyAsync(LibraryContext db, CancellationToken cancellationToken = default)
    {
        await AddColumnIfMissingAsync(db, "ImageUrl", """ALTER TABLE "Products" ADD COLUMN "ImageUrl" TEXT NULL;""", cancellationToken);
        await AddColumnIfMissingAsync(db, "TikTokId", """ALTER TABLE "Products" ADD COLUMN "TikTokId" TEXT NULL;""", cancellationToken);

        await BackfillTikTokIdsAsync(db, cancellationToken);

        foreach (var col in LegacyColumnsToDrop)
            await DropColumnIfExistsAsync(db, col, cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        LibraryContext db,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        long hasColumn;
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = columnName switch
            {
                "ImageUrl" => """
                    SELECT COUNT(1) FROM pragma_table_info('Products') WHERE name='ImageUrl';
                    """,
                "TikTokId" => """
                    SELECT COUNT(1) FROM pragma_table_info('Products') WHERE name='TikTokId';
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(columnName), columnName, null),
            };
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            hasColumn = Convert.ToInt64(scalar ?? 0L);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (hasColumn > 0)
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Already applied
        }
    }

    private static async Task BackfillTikTokIdsAsync(LibraryContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Products" SET "TikTokId" = "TikTokProductId"
                WHERE ("TikTokId" IS NULL OR TRIM("TikTokId") = '')
                  AND "TikTokProductId" IS NOT NULL AND TRIM("TikTokProductId") != '';
                """,
                cancellationToken);
        }
        catch (SqliteException)
        {
            // Column or table missing on very old DBs
        }
    }

    private static async Task DropColumnIfExistsAsync(LibraryContext db, string columnName, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        long exists;
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            // columnName is from an internal whitelist only
            cmd.CommandText = $"""
                SELECT COUNT(1) FROM pragma_table_info('Products') WHERE name='{columnName.Replace("'", "''", StringComparison.Ordinal)}';
                """;
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            exists = Convert.ToInt64(scalar ?? 0L);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (exists == 0)
            return;

        try
        {
            var dropSql = columnName switch
            {
                "Category" => """ALTER TABLE "Products" DROP COLUMN "Category";""",
                "StockQuantity" => """ALTER TABLE "Products" DROP COLUMN "StockQuantity";""",
                "Weight" => """ALTER TABLE "Products" DROP COLUMN "Weight";""",
                "IsBundle" => """ALTER TABLE "Products" DROP COLUMN "IsBundle";""",
                "BundleItems" => """ALTER TABLE "Products" DROP COLUMN "BundleItems";""",
                "Rarity" => """ALTER TABLE "Products" DROP COLUMN "Rarity";""",
                "TikTokProductId" => """ALTER TABLE "Products" DROP COLUMN "TikTokProductId";""",
                _ => throw new InvalidOperationException($"Unexpected legacy column: {columnName}"),
            };
            await db.Database.ExecuteSqlRawAsync(dropSql, cancellationToken);
        }
        catch (SqliteException)
        {
            // Older SQLite or unexpected schema — leave table as-is
        }
    }
}
