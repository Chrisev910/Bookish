using FantasyBooks.Data;
using FantasyBooks.Services;
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

    [BindProperty]
    public IFormFile? ImageFile { get; set; }

    [BindProperty]
    public bool RemoveUploadedImage { get; set; }

    public string? CurrentImageSrc { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.Price,
                p.ImageUrl,
                p.ImageContentType,
                p.TikTokId,
                p.TikTokVideoUrl,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return RedirectToPage("./Index");

        Id = product.Id;
        Input = new CreateModel.ProductInput
        {
            Name = product.Name,
            // Keep HTML for Quill; sanitize so the editor only sees safe markup.
            Description = DescriptionHtml.Sanitize(product.Description),
            Price = product.Price,
            ImageUrl = product.ImageUrl,
            TikTokId = product.TikTokId,
            TikTokVideoUrl = product.TikTokVideoUrl,
        };
        CurrentImageSrc = !string.IsNullOrWhiteSpace(product.ImageContentType)
            ? $"/media/products/{product.Id}"
            : product.ImageUrl;
        ViewData["CurrentImageSrc"] = CurrentImageSrc;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["LibraryDatabase"] = dbInfo.Description;
        if (ImageFile is null or { Length: 0 }
            && !string.IsNullOrWhiteSpace(Input.ImageUrl)
            && (!Uri.TryCreate(Input.ImageUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            ModelState.AddModelError("Input.ImageUrl", "Enter a valid http(s) image URL, or leave blank.");
        }

        if (!string.IsNullOrWhiteSpace(Input.TikTokVideoUrl)
            && (!Uri.TryCreate(Input.TikTokVideoUrl.Trim(), UriKind.Absolute, out var videoUri)
                || (videoUri.Scheme != Uri.UriSchemeHttp && videoUri.Scheme != Uri.UriSchemeHttps)
                || !videoUri.Host.Contains("tiktok", StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError("Input.TikTokVideoUrl", "Enter a full TikTok video URL, or leave blank.");
        }

        if (!ModelState.IsValid)
        {
            CurrentImageSrc = ViewData["CurrentImageSrc"] as string;
            return Page();
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == Id, cancellationToken);
        if (product is null)
        {
            TempData["FlashMessage"] = "That product no longer exists.";
            return RedirectToPage("./Index");
        }

        product.Name = Input.Name.Trim();
        product.Description = DescriptionHtml.Sanitize(Input.Description);
        product.Price = Input.Price;
        product.TikTokId = NullIfEmpty(Input.TikTokId);
        product.TikTokVideoUrl = NullIfEmpty(Input.TikTokVideoUrl);

        byte[]? newImageData = null;
        string? newImageContentType = null;
        var clearImage = RemoveUploadedImage;

        if (ImageFile is { Length: > 0 })
        {
            try
            {
                var uploaded = await ProductImageUpload.ReadAsync(ImageFile, cancellationToken);
                if (uploaded is { } u)
                {
                    newImageData = u.Data;
                    newImageContentType = u.ContentType;
                    clearImage = false;
                    product.ImageUrl = null;
                }
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(ImageFile), ex.Message);
                CurrentImageSrc = ProductImageSrc.Resolve(product);
                ViewData["CurrentImageSrc"] = CurrentImageSrc;
                return Page();
            }
        }
        else if (string.IsNullOrWhiteSpace(product.ImageContentType) || clearImage)
        {
            product.ImageUrl = NullIfEmpty(Input.ImageUrl);
        }
        else if (!string.IsNullOrWhiteSpace(Input.ImageUrl) && product.ImageData is null)
        {
            product.ImageUrl = NullIfEmpty(Input.ImageUrl);
        }

        // Avoid EF binding ImageData byte[] (breaks on Turso); persist blobs separately.
        await db.SaveChangesAsync(cancellationToken);

        if (clearImage)
            await ProductImageBlobStore.ClearAsync(db, product.Id, cancellationToken);
        else if (newImageData is not null && newImageContentType is not null)
            await ProductImageBlobStore.SaveAsync(db, product.Id, newImageData, newImageContentType, cancellationToken);

        TempData["FlashMessage"] = $"Updated “{product.Name}”.";
        return RedirectToPage("./Index");
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
