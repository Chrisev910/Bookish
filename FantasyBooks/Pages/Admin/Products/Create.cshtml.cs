using System.ComponentModel.DataAnnotations;
using FantasyBooks.Data;
using FantasyBooks.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyBooks.Pages.Admin.Products;

public class CreateModel(LibraryContext db, LibraryDatabaseInfo dbInfo) : PageModel
{
    [BindProperty]
    public ProductInput Input { get; set; } = new();

    public void OnGet()
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        ValidateImageUrl();
        if (!ModelState.IsValid)
            return Page();

        db.Products.Add(new Product
        {
            Name = Input.Name.Trim(),
            Description = NullIfEmpty(Input.Description),
            Price = Input.Price,
            ImageUrl = NullIfEmpty(Input.ImageUrl),
            TikTokId = NullIfEmpty(Input.TikTokId),
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["FlashMessage"] = $"Created “{Input.Name.Trim()}”.";
        return RedirectToPage("./Index");
    }

    private void ValidateImageUrl()
    {
        if (string.IsNullOrWhiteSpace(Input.ImageUrl))
            return;
        if (!Uri.TryCreate(Input.ImageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ModelState.AddModelError("Input.ImageUrl", "Enter a valid http(s) image URL, or leave blank.");
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class ProductInput
    {
        [Required]
        [StringLength(200)]
        [Display(Name = "Name")]
        public string Name { get; set; } = "";

        [Display(Name = "Description")]
        [StringLength(8000)]
        public string? Description { get; set; }

        [Required]
        [Range(0.01, 100000)]
        [Display(Name = "Price (GBP)")]
        public decimal Price { get; set; }

        [Display(Name = "Image URL")]
        [StringLength(2000)]
        public string? ImageUrl { get; set; }

        [Display(Name = "TikTok product ID")]
        [StringLength(100)]
        public string? TikTokId { get; set; }
    }
}
