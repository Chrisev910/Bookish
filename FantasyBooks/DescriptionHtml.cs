using Ganss.Xss;

namespace FantasyBooks;

/// <summary>Sanitizes product description HTML for safe storage and display.</summary>
public static class DescriptionHtml
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var cleaned = Sanitizer.Sanitize(html).Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "<p><br></p>" or "<p></p>")
            return null;

        return cleaned;
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "a", "h2", "h3" })
            s.AllowedTags.Add(tag);

        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedAttributes.Add("target");
        s.AllowedAttributes.Add("rel");

        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");

        return s;
    }
}
