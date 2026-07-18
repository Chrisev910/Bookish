namespace FantasyBooks.Models;

public class TikTokVideo
{
    public int Id { get; set; }

    public string VideoUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
