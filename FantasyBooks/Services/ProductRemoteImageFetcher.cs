using FantasyBooks.Models;

namespace FantasyBooks.Services;

/// <summary>Downloads remote product images (e.g. TikTok CDN) into DB blobs.</summary>
public sealed class ProductRemoteImageFetcher
{
    public const string HttpClientName = "ProductRemoteImages";

    /// <summary>Remote product photos are often larger than admin uploads; allow up to 8 MB.</summary>
    public const long MaxRemoteBytes = 8 * 1024 * 1024;

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
    /// On success: sets <see cref="Product.ImageData"/> / <see cref="Product.ImageContentType"/> and clears <see cref="Product.ImageUrl"/>.
    /// On failure: keeps/sets <see cref="Product.ImageUrl"/> and returns a short reason.
    /// </summary>
    public async Task<string?> ApplyAsync(Product product, string? imageUrl, CancellationToken cancellationToken = default)
    {
        var url = NormalizeUrl(imageUrl);
        if (url is null)
            return "missing or invalid image URL";

        var hasBlob = product.HasUploadedImage;
        var previousUrl = NormalizeUrl(product.ImageUrl);

        // Blob present and either already cached (URL cleared) or same remote URL as last attempt.
        if (hasBlob && (previousUrl is null || UrlsEqual(previousUrl, url)))
            return null;

        var (downloaded, error) = await TryDownloadAsync(url, cancellationToken);
        if (downloaded is null)
        {
            product.ImageUrl = url;
            return error ?? "download failed";
        }

        product.ImageData = downloaded.Value.Data;
        product.ImageContentType = downloaded.Value.ContentType;
        product.ImageUrl = null;
        return null;
    }

    public async Task<((byte[] Data, string ContentType)? Result, string? Error)> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken = default)
    {
        var candidates = BuildCandidateUrls(imageUrl);
        string? lastError = null;

        foreach (var candidate in candidates)
        {
            var (result, error) = await TryDownloadOneAsync(candidate, cancellationToken);
            if (result is not null)
                return (result, null);

            lastError = error;
        }

        return (null, lastError ?? "download failed");
    }

    private async Task<((byte[] Data, string ContentType)? Result, string? Error)> TryDownloadOneAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, "invalid URL");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Referer", "https://seller.tiktok.com/");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-GB,en;q=0.9");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var msg = $"HTTP {(int)response.StatusCode} from CDN";
                _logger.LogWarning("Remote image download failed ({StatusCode}): {Url}", (int)response.StatusCode, uri);
                return (null, msg);
            }

            if (response.Content.Headers.ContentLength is > MaxRemoteBytes)
            {
                var msg = $"image too large ({response.Content.Headers.ContentLength} bytes)";
                _logger.LogWarning("Remote image too large ({Length} bytes): {Url}", response.Content.Headers.ContentLength, uri);
                return (null, msg);
            }

            var headerType = NormalizeContentType(response.Content.Headers.ContentType?.MediaType)
                ?? GuessContentType(uri.AbsolutePath);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var ms = new MemoryStream(capacity: 64 * 1024);
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > MaxRemoteBytes)
                {
                    _logger.LogWarning("Remote image exceeded size limit while reading: {Url}", uri);
                    return (null, $"image too large (>{MaxRemoteBytes} bytes)");
                }

                ms.Write(buffer, 0, read);
            }

            if (ms.Length == 0)
                return (null, "empty response");

            var bytes = ms.ToArray();
            var contentType = headerType ?? GuessContentTypeFromBytes(bytes);
            // TikTok sometimes sends octet-stream; trust magic bytes.
            if (contentType is null || !AllowedContentTypes.Contains(contentType))
            {
                contentType = GuessContentTypeFromBytes(bytes);
            }

            if (contentType is null || !AllowedContentTypes.Contains(contentType))
            {
                var msg = $"unsupported type ({headerType ?? "unknown"})";
                _logger.LogWarning("Remote image has unsupported type ({ContentType}): {Url}", headerType ?? "(unknown)", uri);
                return (null, msg);
            }

            return ((bytes, contentType), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex, "Remote image download error: {Url}", uri);
            return (null, ex is TaskCanceledException ? "download timed out" : $"network error: {ex.Message}");
        }
    }

    /// <summary>Prefer a moderately sized TikTok resize variant, then the original URL.</summary>
    private static IReadOnlyList<string> BuildCandidateUrls(string imageUrl)
    {
        var primary = NormalizeUrl(imageUrl);
        if (primary is null)
            return [];

        var list = new List<string>();
        var resized = TryTikTokResizeVariant(primary, 1000);
        if (resized is not null && !UrlsEqual(resized, primary))
            list.Add(resized);

        list.Add(primary);
        return list;
    }

    private static string? TryTikTokResizeVariant(string url, int maxEdge)
    {
        // ...~tplv-{id}-origin-jpeg.jpeg → ...~tplv-{id}-resize-jpeg:{n}:{n}.jpeg
        const string marker = "-origin-jpeg.";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var prefix = url[..idx];
        var suffix = url[(idx + marker.Length - 1)..]; // keep leading '.'
        return $"{prefix}-resize-jpeg:{maxEdge}:{maxEdge}{suffix}";
    }

    private static string? NormalizeUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        // Excel / TikTok exports sometimes join several URLs.
        var raw = imageUrl.Trim();
        foreach (var sep in new[] { '|', '\n', '\r', ';', ',' })
        {
            var cut = raw.IndexOf(sep);
            if (cut > 0)
                raw = raw[..cut].Trim();
        }

        if (raw.StartsWith("//", StringComparison.Ordinal))
            raw = "https:" + raw;

        return string.IsNullOrWhiteSpace(raw) ? null : raw;
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
        // TikTok paths often end in ".jpeg" after long template names.
        if (path is not null && path.Contains("jpeg", StringComparison.OrdinalIgnoreCase) && ext is ".jpeg" or ".jpg" or "")
            return "image/jpeg";

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
