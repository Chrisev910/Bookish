using FantasyBooks.Models;
using Microsoft.EntityFrameworkCore;

namespace FantasyBooks.Data;

public class LibraryContext(DbContextOptions<LibraryContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasColumnType("TEXT");

            entity.Property(e => e.ImageUrl).HasColumnType("TEXT");

            entity.Property(e => e.Description).HasColumnType("TEXT");

            entity.Property(e => e.TikTokId).HasColumnType("TEXT");

            entity.Property(e => e.Price).HasPrecision(18, 2);
        });
    }
}
