using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Web.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Monster> Monsters => Set<Monster>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<RequestLog> RequestLogs => Set<RequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Name).HasMaxLength(200).IsRequired();
            e.Property(g => g.Platform).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Character>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(c => new { c.Name, c.GameId }).IsUnique();
            // Games filter on Popularity to skip obscure NPCs, then pick by offset.
            e.HasIndex(c => c.Popularity);
            e.HasOne(c => c.Game)
             .WithMany(g => g.Characters)
             .HasForeignKey(c => c.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Card>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Element).HasMaxLength(50);
            e.Property(c => c.CardClass).HasMaxLength(50);
            e.HasIndex(c => new { c.Name, c.GameId }).IsUnique();
            e.HasOne(c => c.Game)
             .WithMany(g => g.Cards)
             .HasForeignKey(c => c.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Monster>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(m => new { m.Name, m.GameId }).IsUnique();
            e.HasOne(m => m.Game)
             .WithMany(g => g.Monsters)
             .HasForeignKey(m => m.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequestLog>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Path).HasMaxLength(500).IsRequired();
            e.Property(r => r.Method).HasMaxLength(10).IsRequired();
            e.Property(r => r.ResourceType).HasMaxLength(50);
            e.Property(r => r.SearchTerm).HasMaxLength(500);
            e.Property(r => r.IpHash).HasMaxLength(64);
            e.HasIndex(r => r.Timestamp);
        });
    }
}
