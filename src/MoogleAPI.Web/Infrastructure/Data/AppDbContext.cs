using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Web.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Monster> Monsters => Set<Monster>();

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
            e.HasOne(c => c.Game)
             .WithMany(g => g.Characters)
             .HasForeignKey(c => c.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Monster>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).HasMaxLength(200).IsRequired();
            e.HasOne(m => m.Game)
             .WithMany(g => g.Monsters)
             .HasForeignKey(m => m.GameId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
