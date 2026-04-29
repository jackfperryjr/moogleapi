using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

public class MonsterScraper(AppDbContext db, WikiClient wiki, ILogger<MonsterScraper> logger)
{
    private static readonly Dictionary<string, string> GameCategories = new()
    {
        ["Final Fantasy"]      = "Enemies in Final Fantasy",
        ["Final Fantasy II"]   = "Enemies in Final Fantasy II",
        ["Final Fantasy III"]  = "Enemies in Final Fantasy III",
        ["Final Fantasy IV"]   = "Enemies in Final Fantasy IV",
        ["Final Fantasy V"]    = "Enemies in Final Fantasy V",
        ["Final Fantasy VI"]   = "Enemies in Final Fantasy VI",
        ["Final Fantasy VII"]  = "Enemies in Final Fantasy VII",
        ["Final Fantasy VIII"] = "Enemies in Final Fantasy VIII",
        ["Final Fantasy IX"]   = "Enemies in Final Fantasy IX",
        ["Final Fantasy X"]    = "Enemies in Final Fantasy X",
        ["Final Fantasy XI"]   = "Enemies in Final Fantasy XI",
        ["Final Fantasy XII"]  = "Enemies in Final Fantasy XII",
        ["Final Fantasy XIII"] = "Enemies in Final Fantasy XIII",
        ["Final Fantasy XIV"]  = "Enemies in Final Fantasy XIV",
        ["Final Fantasy XV"]   = "Enemies in Final Fantasy XV",
        ["Final Fantasy XVI"]  = "Enemies in Final Fantasy XVI",
    };

    public async Task ScrapeAsync(CancellationToken ct = default)
    {
        var games = await db.Games.ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameCategories.TryGetValue(game.Name, out var category))
                continue;

            logger.LogInformation("Scraping monsters for {Game}...", game.Name);

            var members = await wiki.GetCategoryMembersAsync(category, ct);
            logger.LogInformation("  Found {Count} candidates", members.Count);

            foreach (var member in members)
            {
                if (member.Title.Contains('/'))
                    continue;

                var name = member.Title.Replace("(Final Fantasy", "").Trim(' ', ')');

                var existing = await db.Monsters
                    .FirstOrDefaultAsync(m => m.Name == name && m.GameId == game.Id, ct);

                if (existing is null)
                {
                    var extract = await wiki.GetExtractAsync(member.Title, ct);
                    db.Monsters.Add(new Monster
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

        logger.LogInformation("Monsters done.");
    }
}
