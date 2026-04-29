using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

public class MonsterScraper(AppDbContext db, WikiClient wiki, ILogger<MonsterScraper> logger)
{
    private static readonly Dictionary<string, string> GameCategories = new()
    {
        ["Final Fantasy"]      = "Final Fantasy enemies",
        ["Final Fantasy IV"]   = "Final Fantasy IV enemies",
        ["Final Fantasy V"]    = "Final Fantasy V enemies",
        ["Final Fantasy VI"]   = "Final Fantasy VI enemies",
        ["Final Fantasy VII"]  = "Final Fantasy VII enemies",
        ["Final Fantasy VIII"] = "Final Fantasy VIII enemies",
        ["Final Fantasy IX"]   = "Final Fantasy IX enemies",
        ["Final Fantasy X"]    = "Final Fantasy X enemies",
        ["Final Fantasy XII"]  = "Final Fantasy XII enemies",
        ["Final Fantasy XIII"] = "Final Fantasy XIII enemies",
        ["Final Fantasy XV"]   = "Final Fantasy XV enemies",
        ["Final Fantasy XVI"]  = "Final Fantasy XVI enemies",
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
