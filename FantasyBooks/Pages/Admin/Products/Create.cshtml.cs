using System.ComponentModel.DataAnnotations;
using FantasyBooks.Data;
using FantasyBooks.Models;
using FantasyBooks.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyBooks.Pages.Admin.Products;

public class CreateModel(LibraryContext db, LibraryDatabaseInfo dbInfo) : PageModel
{
    [BindProperty]
    public ProductInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    [BindProperty]
    public List<IFormFile>? GalleryFiles { get; set; }

    [BindProperty]
    public List<ProductOptionStore.GroupInput>? OptionGroups { get; set; }

    public void OnGet()
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        ViewData["OptionGroups"] = new List<ProductOptionStore.GroupInput>();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        ViewData["OptionGroups"] = OptionGroups ?? [];
        ValidateImageUrl();
        ValidateTikTokVideoUrl();

        var parsedOptions = ProductOptionStore.ParsePosted(
            OptionGroups,
            (key, message) => ModelState.AddModelError("OptionGroups", message));

        if (!ModelState.IsValid || parsedOptions is null)
            return Page();

        byte[]? imageData = null;
        string? imageContentType = null;
        if (ImageFile is { Length: > 0 })
        {
            try
            {
                var uploaded = await ProductImageUpload.ReadAsync(ImageFile, cancellationToken);
                if (uploaded is { } u)
                {
                    imageData = u.Data;
                    imageContentType = u.ContentType;
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(ImageFile), ex.Message);
                return Page();
            }
        }

        var product = new Product
        {
            Name = Input.Name.Trim(),
            Description = DescriptionHtml.Sanitize(Input.Description),
            Price = Input.Price,
            ImageUrl = imageData is null ? NullIfEmpty(Input.ImageUrl) : null,
            TikTokId = NullIfEmpty(Input.TikTokId),
            TikTokVideoUrl = NullIfEmpty(Input.TikTokVideoUrl),
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        if (imageData is not null && imageContentType is not null)
        {
            await ProductImageBlobStore.SaveAsync(db, product.Id, imageData, imageContentType, cancellationToken);
        }

        try
        {
            if (GalleryFiles is { Count: > 0 })
                await ProductImageBlobStore.SaveGalleryUploadsAsync(db, product.Id, GalleryFiles, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            TempData["FlashMessage"] = $"Created “{product.Name}”, but a gallery image was skipped: {ex.Message}";
            return RedirectToPage("./Index");
        }

        await ProductOptionStore.ReplaceAsync(db, product.Id, parsedOptions, cancellationToken);

        TempData["FlashMessage"] = $"Created “{Input.Name.Trim()}”.";
        return RedirectToPage("./Index");
    }

    private void ValidateImageUrl()
    {
        if (ImageFile is { Length: > 0 } || string.IsNullOrWhiteSpace(Input.ImageUrl))
            return;
        if (!Uri.TryCreate(Input.ImageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ModelState.AddModelError("Input.ImageUrl", "Enter a valid http(s) image URL, or leave blank.");
        }
    }

    private void ValidateTikTokVideoUrl()
    {
        if (string.IsNullOrWhiteSpace(Input.TikTokVideoUrl))
            return;
        if (!Uri.TryCreate(Input.TikTokVideoUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !uri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.TikTokVideoUrl", "Enter a full TikTok video URL, or leave blank.");
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

        [Display(Name = "Image URL (optional)")]
        [StringLength(2000)]
        public string? ImageUrl { get; set; }

        [Display(Name = "TikTok product ID")]
        [StringLength(100)]
        public string? TikTokId { get; set; }

        [Display(Name = "Product TikTok video (optional)")]
        [StringLength(2000)]
        public string? TikTokVideoUrl { get; set; }
    }
}
