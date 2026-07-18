namespace FantasyBooks.Options;

public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "bookish";

    /// <summary>Plain password from config/env. Hashed in memory at startup for verification.</summary>
    public string Password { get; set; } = "Ink&Paper2026!";
}
