using System.Net;
using System.Text.RegularExpressions;

namespace FantasyBooks;

/// <summary>Strips HTML from TikTok-style product descriptions for safe plain-text display.</summary>
public static class HtmlPlainText
{
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var s = WebUtility.HtmlDecode(html.Trim());

        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"<p[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"</li\s*>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        s = TagRegex.Replace(s, string.Empty);

        s = Regex.Replace(s, "[ \t]+\n", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    /// <summary>Single-line friendly preview for card grids.</summary>
    public static string PreviewForCard(string? html)
    {
        var s = FromHtml(html);
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
