using System.Data;
using System.Data.Common;
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

        // Hex is only 0-9A-F — safe to embed in SQL.
        var hex = Convert.ToHexString(data);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE \"Products\" SET \"ImageData\" = X'" + hex
                + "', \"ImageContentType\" = @ct, \"ImageUrl\" = NULL WHERE \"Id\" = @id";

            AddParam(cmd, "@ct", contentType.Trim());
            AddParam(cmd, "@id", productId);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (rows == 0)
                throw new InvalidOperationException($"No product row updated for id {productId}.");
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        // Keep EF's tracker from trying to re-save a stale ImageData parameter binding.
        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = data;
            tracked.ImageContentType = contentType.Trim();
            tracked.ImageUrl = null;
            db.Entry(tracked).State = EntityState.Unchanged;
        }
    }

    public static async Task ClearAsync(
        LibraryContext db,
        int productId,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                UPDATE "Products"
                SET "ImageData" = NULL,
                    "ImageContentType" = NULL
                WHERE "Id" = @id
                """;
            AddParam(cmd, "@id", productId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = null;
            tracked.ImageContentType = null;
            db.Entry(tracked).State = EntityState.Unchanged;
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
