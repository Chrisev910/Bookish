using FantasyBooks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Controllers;

[Route("media/products")]
public class ProductImageController(LibraryContext db) : Controller
{
    [HttpGet("{id:int}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var row = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.ImageData, p.ImageContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.ImageData is not { Length: > 0 } || string.IsNullOrWhiteSpace(row.ImageContentType))
            return NotFound();

        return File(row.ImageData, row.ImageContentType);
    }
}
