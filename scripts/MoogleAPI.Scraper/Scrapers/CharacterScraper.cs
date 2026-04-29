using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

public class CharacterScraper(AppDbContext db, WikiClient wiki, ILogger<CharacterScraper> logger)
{
    // Maps each game's DB name → wiki category name for characters
    private static readonly Dictionary<string, string> GameCategories = new()
    {
        ["Final Fantasy"]      = "Final Fantasy characters",
        ["Final Fantasy IV"]   = "Final Fantasy IV characters",
        ["Final Fantasy V"]    = "Final Fantasy V characters",
        ["Final Fantasy VI"]   = "Final Fantasy VI characters",
        ["Final Fantasy VII"]  = "Final Fantasy VII characters",
        ["Final Fantasy VIII"] = "Final Fantasy VIII characters",
        ["Final Fantasy IX"]   = "Final Fantasy IX characters",
        ["Final Fantasy X"]    = "Final Fantasy X characters",
        ["Final Fantasy XII"]  = "Final Fantasy XII characters",
        ["Final Fantasy XIII"] = "Final Fantasy XIII characters",
        ["Final Fantasy XV"]   = "Final Fantasy XV characters",
        ["Final Fantasy XVI"]  = "Final Fantasy XVI characters",
    };

    public async Task ScrapeAsync(CancellationToken ct = default)
    {
        var games = await db.Games.ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameCategories.TryGetValue(game.Name, out var category))
                continue;

            logger.LogInformation("Scraping characters for {Game}...", game.Name);

            var members = await wiki.GetCategoryMembersAsync(category, ct);
            logger.LogInformation("  Found {Count} candidates", members.Count);

            foreach (var member in members)
            {
                // Skip meta/disambiguation pages
                if (member.Title.Contains('/') || member.Title.Contains('('))
                    continue;

                var name = member.Title.Replace("(Final Fantasy", "").Trim(' ', ')');

                var existing = await db.Characters
                    .FirstOrDefaultAsync(c => c.Name == name && c.GameId == game.Id, ct);

                if (existing is null)
                {
                    var extract = await wiki.GetExtractAsync(member.Title, ct);
                    db.Characters.Add(new Character
                    {
                        Name = name,
                        Description = extract,
                        GameId = game.Id
                    });
                    logger.LogInformation("  + {Name}", name);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Characters done.");
    }
}
