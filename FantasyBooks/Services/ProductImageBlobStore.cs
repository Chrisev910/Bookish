using System.Data;
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
    public const int MaxGalleryImagesPerProduct = 8;

    public static async Task SaveAsync(
        LibraryContext db,
        int productId,
        byte[] data,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var ct = NormalizeContentType(contentType);
        var hex = Convert.ToHexString(data);
        var sql =
            "UPDATE \"Products\" SET \"ImageData\" = X'" + hex
            + "', \"ImageContentType\" = '" + ct
            + "', \"ImageUrl\" = NULL, \"ImageRevision\" = COALESCE(\"ImageRevision\", 0) + 1 WHERE \"Id\" = "
            + productId;

        await ExecuteAsync(db, sql, requireRows: true, cancellationToken);

        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = data;
            tracked.ImageContentType = ct;
            tracked.ImageUrl = null;
            tracked.ImageRevision += 1;
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
            "UPDATE \"Products\" SET \"ImageData\" = NULL, \"ImageContentType\" = NULL, "
            + "\"ImageRevision\" = COALESCE(\"ImageRevision\", 0) + 1 WHERE \"Id\" = " + productId,
            requireRows: true,
            cancellationToken);

        var tracked = db.Products.Local.FirstOrDefault(p => p.Id == productId);
        if (tracked is not null)
        {
            tracked.ImageData = null;
            tracked.ImageContentType = null;
            tracked.ImageRevision += 1;
            db.Entry(tracked).State = EntityState.Unchanged;
        }
    }

    public static async Task<int> AddGalleryImageAsync(
        LibraryContext db,
        int productId,
        byte[] data,
        string contentType,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        var ct = NormalizeContentType(contentType);
        var hex = Convert.ToHexString(data);
        var sql =
            "INSERT INTO \"ProductGalleryImages\" (\"ProductId\", \"SortOrder\", \"ContentType\", \"ImageData\") "
            + "VALUES (" + productId + ", " + sortOrder + ", '" + ct + "', X'" + hex + "')";

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT last_insert_rowid()";
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(result);
            }
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public static async Task DeleteGalleryImageAsync(
        LibraryContext db,
        int productId,
        int galleryImageId,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            db,
            "DELETE FROM \"ProductGalleryImages\" WHERE \"Id\" = " + galleryImageId
            + " AND \"ProductId\" = " + productId,
            requireRows: false,
            cancellationToken);

        var tracked = db.ProductGalleryImages.Local.FirstOrDefault(g => g.Id == galleryImageId);
        if (tracked is not null)
            db.Entry(tracked).State = EntityState.Detached;
    }

    public static async Task SaveGalleryUploadsAsync(
        LibraryContext db,
        int productId,
        IEnumerable<IFormFile?> files,
        CancellationToken cancellationToken = default)
    {
        var existingCount = await db.ProductGalleryImages.CountAsync(g => g.ProductId == productId, cancellationToken);
        var sort = existingCount;
        foreach (var file in files)
        {
            if (file is null || file.Length == 0)
                continue;
            if (sort >= MaxGalleryImagesPerProduct)
                break;

            var uploaded = await ProductImageUpload.ReadAsync(file, cancellationToken);
            if (uploaded is null)
                continue;

            await AddGalleryImageAsync(db, productId, uploaded.Value.Data, uploaded.Value.ContentType, sort, cancellationToken);
            sort++;
        }
    }

    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            throw new InvalidOperationException("Image content type is required.");
        var ct = contentType.Trim();
        if (ct is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))
            throw new InvalidOperationException($"Unsupported image content type: {ct}");
        return ct;
    }

    private static async Task ExecuteAsync(
        LibraryContext db,
        string sql,
        bool requireRows,
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
            if (requireRows && rows == 0)
                throw new InvalidOperationException("No product row was updated.");
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
