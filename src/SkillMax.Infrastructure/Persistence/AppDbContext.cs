using Microsoft.EntityFrameworkCore;
using SkillMax.Domain.Entities;

namespace SkillMax.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<SearchTerm> SearchTerms => Set<SearchTerm>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<UnderstoodPage> UnderstoodPages => Set<UnderstoodPage>();
    public DbSet<UnderstoodShort> UnderstoodShorts => Set<UnderstoodShort>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Book>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.FilePath).IsRequired().HasMaxLength(1024);
            e.Property(x => x.ColorHex).HasMaxLength(9);
            e.HasIndex(x => x.IsArchived);
            e.HasIndex(x => x.CategoryId);
        });

        // No FK relationship is declared for CategoryId on any of the three. Databases that
        // predate categories get the column through ALTER TABLE, and SQLite can't add a foreign
        // key that way — so a fresh install would enforce a constraint an upgraded one doesn't.
        // CategoryService clears the references itself when a category is deleted, which holds
        // either way.
        b.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(60);
            e.Property(x => x.ColorHex).HasMaxLength(9);
        });

        b.Entity<Channel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Handle).IsRequired().HasMaxLength(60);
            e.Property(x => x.ChannelId).IsRequired().HasMaxLength(40);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.AvatarUrl).HasMaxLength(500);
            e.HasIndex(x => x.ChannelId).IsUnique();   // one row per channel, however it was named
            e.HasIndex(x => x.CategoryId);
        });

        b.Entity<SearchTerm>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Query).IsRequired().HasMaxLength(200);
            e.Property(x => x.Normalized).IsRequired().HasMaxLength(200);
            e.HasIndex(x => x.Normalized).IsUnique();   // "12 Week Year" and "12 week year" are one search
            e.HasIndex(x => x.CategoryId);
        });

        b.Entity<SavedCard>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookId, x.PageIndex }).IsUnique();  // idempotent save-by-page
        });

        b.Entity<UnderstoodPage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BookId, x.PageIndex }).IsUnique();   // idempotent mark-by-page
        });

        b.Entity<UnderstoodShort>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.VideoId).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.VideoId).IsUnique();
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(120);
            e.Property(x => x.Value).IsRequired().HasMaxLength(2000);
        });
    }
}
