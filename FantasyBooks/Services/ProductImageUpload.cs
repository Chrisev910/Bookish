namespace FantasyBooks.Services;

public static class ProductImageUpload
{
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    public static async Task<(byte[] Data, string ContentType)?> ReadAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return null;

        if (file.Length > MaxBytes)
            throw new InvalidOperationException($"Image must be {MaxBytes / (1024 * 1024)} MB or smaller.");

        var contentType = file.ContentType?.Trim() ?? "";
        if (!AllowedContentTypes.Contains(contentType))
        {
            // Some browsers send empty/octet-stream; sniff by extension.
            contentType = GuessContentType(file.FileName) ?? contentType;
        }

        if (!AllowedContentTypes.Contains(contentType))
            throw new InvalidOperationException("Only JPEG, PNG, WebP, or GIF images are allowed.");

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream(capacity: (int)Math.Min(file.Length, MaxBytes));
        await stream.CopyToAsync(ms, cancellationToken);
        return (ms.ToArray(), contentType);
    }

    private static string? GuessContentType(string? fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };
    }
}
