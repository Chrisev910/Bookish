using System.Globalization;
using FantasyBooks.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FantasyBooks.Data;

public class LibraryContext : DbContext
{
    public LibraryContext(DbContextOptions<LibraryContext> options, LibraryDatabaseInfo dbInfo)
        : base(options)
    {
        // LibSQL transactions report the inner connection; disable EF auto-transactions on Turso.
        if (dbInfo.IsRemoteTurso)
            Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductGalleryImage> ProductGalleryImages => Set<ProductGalleryImage>();

    public DbSet<TikTokVideo> TikTokVideos => Set<TikTokVideo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasColumnType("TEXT");

            entity.Property(e => e.ImageUrl).HasColumnType("TEXT");

            entity.Property(e => e.ImageContentType).HasColumnType("TEXT");

            entity.Property(e => e.ImageData).HasColumnType("BLOB");

            entity.Property(e => e.ImageRevision).HasColumnType("INTEGER");

            entity.Property(e => e.Description).HasColumnType("TEXT");

            entity.Property(e => e.TikTokId).HasColumnType("TEXT");

            entity.Property(e => e.TikTokVideoUrl).HasMaxLength(2000).HasColumnType("TEXT");

            entity.Property(e => e.Price).HasPrecision(18, 2);

            entity.HasMany(e => e.GalleryImages)
                .WithOne(e => e.Product!)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductGalleryImage>(entity =>
        {
            entity.ToTable("ProductGalleryImages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100).HasColumnType("TEXT");
            entity.Property(e => e.ImageData).IsRequired().HasColumnType("BLOB");
            entity.Property(e => e.SortOrder).HasColumnType("INTEGER");
            entity.HasIndex(e => new { e.ProductId, e.SortOrder });
        });

        modelBuilder.Entity<TikTokVideo>(entity =>
        {
            entity.ToTable("TikTokVideos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VideoUrl).IsRequired().HasMaxLength(2000).HasColumnType("TEXT");
            entity.Property(e => e.IsActive).HasColumnType("INTEGER");
            // Store ISO-8601 so en-GB request culture cannot break reads of US-style TEXT dates from Turso/LibSQL.
            entity.Property(e => e.DateCreated)
                .HasColumnType("TEXT")
                .HasConversion(new ValueConverter<DateTime, string>(
                    v => v.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    v => ParseUtcDateTime(v)));
            entity.HasIndex(e => new { e.IsActive, e.DateCreated });
        });
    }

    private static DateTime ParseUtcDateTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.UtcNow;

        var s = raw.Trim();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            || DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt)
            || DateTime.TryParse(s, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            };
        }

        return DateTime.UtcNow;
    }
}
