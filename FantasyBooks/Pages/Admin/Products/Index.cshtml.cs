using FantasyBooks.Data;
using FantasyBooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Pages.Admin.Products;

public class IndexModel(LibraryContext db, LibraryDatabaseInfo dbInfo) : PageModel
{
    public IList<Product> Products { get; private set; } = [];

    public string? FlashMessage { get; private set; }

    public string DatabaseDescription => dbInfo.Description;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        FlashMessage = TempData["FlashMessage"] as string;
        ViewData["LibraryDatabase"] = dbInfo.Description;
        Products = await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            TempData["FlashMessage"] = "That product was already removed.";
            return RedirectToPage();
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        TempData["FlashMessage"] = $"Removed “{product.Name}”.";
        return RedirectToPage();
    }
}
