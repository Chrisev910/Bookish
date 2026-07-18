using FantasyBooks.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Pages.Admin.Products;

public class EditModel(LibraryContext db, LibraryDatabaseInfo dbInfo) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public CreateModel.ProductInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return RedirectToPage("./Index");

        Id = product.Id;
        Input = new CreateModel.ProductInput
        {
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            ImageUrl = product.ImageUrl,
            TikTokId = product.TikTokId,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        if (!string.IsNullOrWhiteSpace(Input.ImageUrl)
            && (!Uri.TryCreate(Input.ImageUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            ModelState.AddModelError("Input.ImageUrl", "Enter a valid http(s) image URL, or leave blank.");
        }

        if (!ModelState.IsValid)
            return Page();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == Id, cancellationToken);
        if (product is null)
        {
            TempData["FlashMessage"] = "That product no longer exists.";
            return RedirectToPage("./Index");
        }

        product.Name = Input.Name.Trim();
        product.Description = NullIfEmpty(Input.Description);
        product.Price = Input.Price;
        product.ImageUrl = NullIfEmpty(Input.ImageUrl);
        product.TikTokId = NullIfEmpty(Input.TikTokId);

        await db.SaveChangesAsync(cancellationToken);
        TempData["FlashMessage"] = $"Updated “{product.Name}”.";
        return RedirectToPage("./Index");
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
