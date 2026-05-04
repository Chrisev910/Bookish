using FantasyBooks.Models;

namespace FantasyBooks.Data;

public static class SeedData
{
    /// <summary>
    /// Removes legacy demo listings and optionally seeds an empty catalog.
    /// </summary>
    public static void Initialize(LibraryContext context)
    {
        var legacyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Dragon's Hoard Bundle",
            "Standard Leather Bookmark",
        };

        var legacy = context.Products.Where(p => legacyNames.Contains(p.Name)).ToList();
        if (legacy.Count > 0)
        {
            context.Products.RemoveRange(legacy);
            context.SaveChanges();
        }

        if (context.Products.Any())
            return;

        // No default products — catalog starts empty until you import.
    }
}
