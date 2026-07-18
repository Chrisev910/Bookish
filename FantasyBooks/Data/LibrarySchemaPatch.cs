using System.Data;
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

        // Ensure critical image columns exist (ALTER can be skipped silently on some Turso errors).
        await EnsureColumnAsync(db, "Products", "ImageContentType", """ALTER TABLE "Products" ADD COLUMN "ImageContentType" TEXT NULL;""", logger, cancellationToken);
        await EnsureColumnAsync(db, "Products", "ImageData", """ALTER TABLE "Products" ADD COLUMN "ImageData" BLOB NULL;""", logger, cancellationToken);

        await TryExecAsync(
            db,
            """
            CREATE TABLE IF NOT EXISTS "TikTokVideos" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TikTokVideos" PRIMARY KEY AUTOINCREMENT,
                "VideoUrl" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "DateCreated" TEXT NOT NULL
            );
            """,
            logger,
            cancellationToken);

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

    /// <summary>
    /// Uses ADO.NET on the open connection instead of EF <c>ExecuteSqlRaw</c>,
    /// which NREs with the Turso/LibSQL connection wrapper.
    /// </summary>
    private static async Task TryExecAsync(
        LibraryContext db,
        string sql,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var conn = await OpenAsync(db, cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Duplicate column / missing legacy column / Turso DDL quirks — never fail startup.
            logger?.LogWarning(ex, "Schema patch statement skipped: {Sql}", sql.ReplaceLineEndings(" ").Trim());
        }
    }

    private static async Task EnsureColumnAsync(
        LibraryContext db,
        string table,
        string column,
        string addColumnSql,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(db, table, column, cancellationToken))
            return;

        logger?.LogWarning("Column {Table}.{Column} missing — applying {Sql}", table, column, addColumnSql.Trim());
        await TryExecAsync(db, addColumnSql, logger, cancellationToken);

        if (!await ColumnExistsAsync(db, table, column, cancellationToken))
        {
            logger?.LogError(
                "Column {Table}.{Column} is still missing after ALTER. Image uploads/caching will fail until the schema is fixed.",
                table,
                column);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        LibraryContext db,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        try
        {
            var conn = await OpenAsync(db, cancellationToken);
            await using var cmd = conn.CreateCommand();
            // PRAGMA table_info works on SQLite and Turso/libSQL.
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // name is column index 1
                if (reader.FieldCount > 1
                    && string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Fall through — caller will attempt ALTER.
        }

        return false;
    }

    private static async Task<System.Data.Common.DbConnection> OpenAsync(
        LibraryContext db,
        CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);
        return conn;
    }
}
