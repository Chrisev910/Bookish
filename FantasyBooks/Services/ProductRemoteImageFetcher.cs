using FantasyBooks.Models;

namespace FantasyBooks.Services;

/// <summary>Downloads remote product images (e.g. TikTok CDN) into DB blobs.</summary>
public sealed class ProductRemoteImageFetcher
{
    public const string HttpClientName = "ProductRemoteImages";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProductRemoteImageFetcher> _logger;

    public ProductRemoteImageFetcher(
        IHttpClientFactory httpClientFactory,
        ILogger<ProductRemoteImageFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Downloads <paramref name="imageUrl"/> when the product needs a (new) blob.
    /// On success: sets <see cref="Product.ImageData"/> / <see cref="Product.ImageContentType"/> and clears <see cref="Product.ImageUrl"/>
    /// so the shop serves <c>/media/products/{id}</c> only (no TikTok CDN dependency).
    /// On failure: sets <see cref="Product.ImageUrl"/> so the shop can still show the remote image.
    /// Skips when a blob already exists and the stored URL is unchanged, or when a blob exists and
    /// <see cref="Product.ImageUrl"/> was already cleared (prior successful cache — re-import same listing).
    /// Re-downloads when a blob exists but <see cref="Product.ImageUrl"/> is still set and differs.
    /// </summary>
    public async Task ApplyAsync(Product product, string? imageUrl, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        if (url is null)
            return;

        var hasBlob = product.HasUploadedImage;
        var previousUrl = string.IsNullOrWhiteSpace(product.ImageUrl) ? null : product.ImageUrl.Trim();

        // Blob present and either already cached (URL cleared) or same remote URL as last attempt.
        if (hasBlob && (previousUrl is null || UrlsEqual(previousUrl, url)))
            return;

        var downloaded = await TryDownloadAsync(url, cancellationToken);
        if (downloaded is null)
        {
            product.ImageUrl = url;
            return;
        }

        product.ImageData = downloaded.Value.Data;
        product.ImageContentType = downloaded.Value.ContentType;
        product.ImageUrl = null;
    }

    public async Task<(byte[] Data, string ContentType)?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Skipping remote image with invalid URL: {Url}", imageUrl);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Remote image download failed ({StatusCode}): {Url}",
                    (int)response.StatusCode,
                    uri);
                return null;
            }

            if (response.Content.Headers.ContentLength is > ProductImageUpload.MaxBytes)
            {
                _logger.LogWarning(
                    "Remote image too large ({Length} bytes): {Url}",
                    response.Content.Headers.ContentLength,
                    uri);
                return null;
            }

            var contentType = NormalizeContentType(response.Content.Headers.ContentType?.MediaType)
                ?? GuessContentType(uri.AbsolutePath);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var ms = new MemoryStream(capacity: (int)Math.Min(ProductImageUpload.MaxBytes, 64 * 1024));
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > ProductImageUpload.MaxBytes)
                {
                    _logger.LogWarning("Remote image exceeded size limit while reading: {Url}", uri);
                    return null;
                }

                ms.Write(buffer, 0, read);
            }

            if (ms.Length == 0)
            {
                _logger.LogWarning("Remote image was empty: {Url}", uri);
                return null;
            }

            var bytes = ms.ToArray();
            contentType ??= GuessContentTypeFromBytes(bytes);
            if (contentType is null || !AllowedContentTypes.Contains(contentType))
            {
                _logger.LogWarning(
                    "Remote image has unsupported type ({ContentType}): {Url}",
                    contentType ?? "(unknown)",
                    uri);
                return null;
            }

            return (bytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex, "Remote image download error: {Url}", uri);
            return null;
        }
    }

    private static bool UrlsEqual(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return null;

        var type = mediaType.Trim();
        if (type.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";

        return AllowedContentTypes.Contains(type) ? type : null;
    }

    private static string? GuessContentType(string? path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null,
        };
    }

    private static string? GuessContentTypeFromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        if (data.Length >= 6
            && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F')
            return "image/gif";
        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return "image/webp";

        return null;
    }
}
