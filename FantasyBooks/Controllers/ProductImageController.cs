using System.Security.Cryptography;
using FantasyBooks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Controllers;

[Route("media/products")]
public class ProductImageController(LibraryContext db) : Controller
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var row = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.ImageData, p.ImageContentType, p.ImageRevision })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.ImageData is not { Length: > 0 } || string.IsNullOrWhiteSpace(row.ImageContentType))
            return NotFound();

        return FileWithEtag(row.ImageData, row.ImageContentType, $"c-{id}-{row.ImageRevision}");
    }

    [HttpGet("{productId:int}/gallery/{imageId:int}")]
    public async Task<IActionResult> GetGallery(int productId, int imageId, CancellationToken cancellationToken)
    {
        var row = await db.ProductGalleryImages.AsNoTracking()
            .Where(g => g.Id == imageId && g.ProductId == productId)
            .Select(g => new { g.ImageData, g.ContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.ImageData is not { Length: > 0 } || string.IsNullOrWhiteSpace(row.ContentType))
            return NotFound();

        return FileWithEtag(row.ImageData, row.ContentType, $"g-{productId}-{imageId}");
    }

    private IActionResult FileWithEtag(byte[] data, string contentType, string etagSeed)
    {
        var hash = Convert.ToHexString(SHA256.HashData(data).AsSpan(0, 8));
        var etagValue = $"\"{etagSeed}-{hash}\"";
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch)
            && (ifNoneMatch == etagValue || ifNoneMatch.Contains(etagValue, StringComparison.Ordinal)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
        Response.Headers.ETag = etagValue;
        return File(data, contentType);
    }
}
