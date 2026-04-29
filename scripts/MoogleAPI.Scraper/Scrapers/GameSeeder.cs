using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

public class GameSeeder(AppDbContext db, ILogger<GameSeeder> logger)
{
    // Mainline titles are stable — seeding beats scraping for this.
    private static readonly Game[] KnownGames =
    [
        new() { Name = "Final Fantasy",       ReleaseYear = 1987, Platform = "NES" },
        new() { Name = "Final Fantasy II",    ReleaseYear = 1988, Platform = "NES" },
        new() { Name = "Final Fantasy III",   ReleaseYear = 1990, Platform = "NES" },
        new() { Name = "Final Fantasy IV",    ReleaseYear = 1991, Platform = "SNES" },
        new() { Name = "Final Fantasy V",     ReleaseYear = 1992, Platform = "SNES" },
        new() { Name = "Final Fantasy VI",    ReleaseYear = 1994, Platform = "SNES" },
        new() { Name = "Final Fantasy VII",   ReleaseYear = 1997, Platform = "PlayStation" },
        new() { Name = "Final Fantasy VIII",  ReleaseYear = 1999, Platform = "PlayStation" },
        new() { Name = "Final Fantasy IX",    ReleaseYear = 2000, Platform = "PlayStation" },
        new() { Name = "Final Fantasy X",     ReleaseYear = 2001, Platform = "PlayStation 2" },
        new() { Name = "Final Fantasy XI",    ReleaseYear = 2002, Platform = "PC / PlayStation 2" },
        new() { Name = "Final Fantasy XII",   ReleaseYear = 2006, Platform = "PlayStation 2" },
        new() { Name = "Final Fantasy XIII",  ReleaseYear = 2009, Platform = "PlayStation 3 / Xbox 360" },
        new() { Name = "Final Fantasy XIV",   ReleaseYear = 2013, Platform = "PC / PlayStation 3" },
        new() { Name = "Final Fantasy XV",    ReleaseYear = 2016, Platform = "PlayStation 4 / Xbox One" },
        new() { Name = "Final Fantasy XVI",   ReleaseYear = 2023, Platform = "PlayStation 5" },
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Seeding {Count} games...", KnownGames.Length);

        foreach (var game in KnownGames)
        {
            var existing = await db.Games.FirstOrDefaultAsync(g => g.Name == game.Name, ct);
            if (existing is null)
            {
                db.Games.Add(game);
                logger.LogInformation("  Added: {Name}", game.Name);
            }
            else
            {
                existing.ReleaseYear = game.ReleaseYear;
                existing.Platform = game.Platform;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Games done.");
    }
}
