using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

public class CharacterScraper(AppDbContext db, WikiClient wiki, ILogger<CharacterScraper> logger)
{
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
            if (!GameCategories.TryGetValue(game.Name, out var category)) continue;

            logger.LogInformation("Scraping characters for {Game}...", game.Name);

            var members = await wiki.GetCategoryMembersAsync(category, ct);

            var candidates = members
                .Where(m => !m.Title.Contains('/'))
                .Select(m => (Member: m, Name: m.Title.Replace("(Final Fantasy", "").Trim(' ', ')')))
                .ToList();

            logger.LogInformation("  Found {Count} candidates", candidates.Count);

            // Pre-load existing characters so DB reads happen off the hot path
            var existing = await db.Characters
                .Where(c => c.GameId == game.Id)
                .ToDictionaryAsync(c => c.Name, ct);

            // Fetch wiki data for new/incomplete characters — 3 concurrent
            var sem = new SemaphoreSlim(3);
            var detailsMap = new ConcurrentDictionary<string, CharacterDetails>();

            await Task.WhenAll(candidates.Select(async item =>
            {
                if (existing.TryGetValue(item.Name, out var ch) && !NeedsEnrichment(ch))
                    return;

                await sem.WaitAsync(ct);
                try
                {
                    var details = await wiki.GetCharacterDetailsAsync(item.Member.Title, ct);
                    detailsMap[item.Name] = details;
                }
                finally { sem.Release(); }
            }));

            // Apply results sequentially (DbContext is not thread-safe)
            foreach (var (member, name) in candidates)
            {
                if (!detailsMap.TryGetValue(name, out var details)) continue;

                if (!existing.TryGetValue(name, out var ch))
                {
                    db.Characters.Add(new Character
                    {
                        Name        = name,
                        Description = details.Description,
                        Role        = details.Role,
                        Affiliation = details.Affiliation,
                        Race        = details.Race,
                        Hometown    = details.Hometown,
                        ImageUrl    = details.ImageUrl,
                        GameId      = game.Id
                    });
                    logger.LogInformation("  + {Name}", name);
                }
                else
                {
                    ch.Description  ??= details.Description;
                    ch.Role         ??= details.Role;
                    ch.Affiliation  ??= details.Affiliation;
                    ch.Race         ??= details.Race;
                    ch.Hometown     ??= details.Hometown;
                    ch.ImageUrl     ??= details.ImageUrl;
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation("  ~ enriched {Name}", name);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Characters done.");
    }

    private static bool NeedsEnrichment(Character c) =>
        c.Description is null || c.ImageUrl is null || c.Role is null;
}
