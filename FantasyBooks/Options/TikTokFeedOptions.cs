namespace FantasyBooks.Options;

/// <summary>
/// Third-party TikTok user-feed sync via RapidAPI (tiktok-scraper7).
/// Set <see cref="RapidApiKey"/> and <see cref="Username"/> in config or env.
/// </summary>
public class TikTokFeedOptions
{
    public const string SectionName = "TikTokFeed";

    /// <summary>TikTok handle without @ (e.g. bookishinkpaper).</summary>
    public string Username { get; set; } = "";

    /// <summary>RapidAPI key from your RapidAPI dashboard.</summary>
    public string RapidApiKey { get; set; } = "";

    public string RapidApiHost { get; set; } = "tiktok-scraper7.p.rapidapi.com";

    /// <summary>How many latest posts to pull into the footer feed.</summary>
    public int TakeCount { get; set; } = 4;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RapidApiKey) && !string.IsNullOrWhiteSpace(Username);
}
