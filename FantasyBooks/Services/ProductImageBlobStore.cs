using System.Data;
using System.Data.Common;
using System.Text;
using FantasyBooks.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Services;

/// <summary>
/// Persists product image BLOBs in a Turso/LibSQL-friendly way.
/// EF Core + LibSQL often fails when binding <c>byte[]</c> parameters over HTTP;
/// inlining a hex blob literal (<c>X'…'</c>) is reliable for both local SQLite and Turso.
/// </summary>
public static class ProductImageBlobStore
{
    public static async Task SaveAsync(
        LibraryContext db,
        int productId,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new InvalidOperationException("Image data is empty.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new InvalidOperationException("Image content type is required.");

        // Only allow known MIME types into SQL text.
        var ct = contentType.Trim();
        if (ct is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))
            throw new InvalidOperationException($"Unsupported image content type: {ct}");

        // Hex is only 0-9A-F — safe to embed in SQL. Avoid binary parameters (Turso/LibSQL HTTP).
        var hex = Convert.ToHexString(data);
        var sql = new StringBuilder(hex.Length + 128);
        sql.Append("UPDATE \"Products\" SET \"ImageData\" = X'");
        sql.Append(hex);
        sql.Append("', \"ImageContentType\" = '");
        sql.Append(ct);
        sql.Append("', \"ImageUrl\" = NULL WHERE \"Id\" = ");
        sql.Append(productId);

        await ExecuteAsync(db, sql.ToString(), cancellationToken);

        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = data;
            tracked.ImageContentType = ct;
            tracked.ImageUrl = null;
            db.Entry(tracked).State = EntityState.Unchanged;
        }
    }

    public static async Task ClearAsync(
        LibraryContext db,
        int productId,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            db,
            """
            UPDATE "Products"
            SET "ImageData" = NULL,
                "ImageContentType" = NULL
            WHERE "Id" =
            """ + productId,
            cancellationToken);

        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = null;
            tracked.ImageContentType = null;
            db.Entry(tracked).State = EntityState.Unchanged;
        }
    }

    private static async Task ExecuteAsync(
        LibraryContext db,
        string sql,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (rows == 0)
                throw new InvalidOperationException("No product row was updated.");
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
