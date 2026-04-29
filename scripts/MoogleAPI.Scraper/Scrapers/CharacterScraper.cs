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
        ["Final Fantasy"]      = "Characters in Final Fantasy",
        ["Final Fantasy II"]   = "Characters in Final Fantasy II",
        ["Final Fantasy III"]  = "Characters in Final Fantasy III",
        ["Final Fantasy IV"]   = "Characters in Final Fantasy IV",
        ["Final Fantasy V"]    = "Characters in Final Fantasy V",
        ["Final Fantasy VI"]   = "Characters in Final Fantasy VI",
        ["Final Fantasy VII"]  = "Characters in Final Fantasy VII",
        ["Final Fantasy VIII"] = "Characters in Final Fantasy VIII",
        ["Final Fantasy IX"]   = "Characters in Final Fantasy IX",
        ["Final Fantasy X"]    = "Characters in Final Fantasy X",
        ["Final Fantasy XI"]   = "Characters in Final Fantasy XI",
        ["Final Fantasy XII"]  = "Characters in Final Fantasy XII",
        ["Final Fantasy XIII"] = "Characters in Final Fantasy XIII",
        ["Final Fantasy XIV"]  = "Characters in Final Fantasy XIV",
        ["Final Fantasy XV"]   = "Characters in Final Fantasy XV",
        ["Final Fantasy XVI"]  = "Characters in Final Fantasy XVI",
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
                // Skip sub-pages (e.g. "Cloud Strife/Artwork")
                if (member.Title.Contains('/'))
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
