using FantasyBooks.Models;
using Microsoft.EntityFrameworkCore;

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

    public DbSet<TikTokVideo> TikTokVideos => Set<TikTokVideo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasColumnType("TEXT");

            entity.Property(e => e.ImageUrl).HasColumnType("TEXT");

            entity.Property(e => e.ImageContentType).HasColumnType("TEXT");

            entity.Property(e => e.ImageData).HasColumnType("BLOB");

            entity.Property(e => e.Description).HasColumnType("TEXT");

            entity.Property(e => e.TikTokId).HasColumnType("TEXT");

            entity.Property(e => e.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<TikTokVideo>(entity =>
        {
            entity.ToTable("TikTokVideos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VideoUrl).IsRequired().HasMaxLength(2000).HasColumnType("TEXT");
            entity.Property(e => e.IsActive).HasColumnType("INTEGER");
            entity.Property(e => e.DateCreated).HasColumnType("TEXT");
            entity.HasIndex(e => new { e.IsActive, e.DateCreated });
        });
    }
}
