using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Marks the characters the player controls in battle, per game.
/// </summary>
/// <remarks>
/// A separate stage rather than part of <see cref="CharacterScraper"/> because the source is a
/// different shape: one navbox article per game listing every playable name, instead of one
/// article per character. Eleven requests settle the whole roster, where doing it per character
/// would mean re-reading two thousand articles to learn something eleven of them already say.
/// </remarks>
public class PlayableScraper(AppDbContext db, WikiClient wiki, ILogger<PlayableScraper> logger)
{
    /// <summary>
    /// The navbox that carries each game's cast. Named by the wiki's own abbreviation, which is
    /// not derivable from the game title we store — hence the table.
    /// </summary>
    private static readonly Dictionary<string, string> GameNavboxes = new()
    {
        ["Final Fantasy"] = "Template:Navbox characters FFI",
        ["Final Fantasy II"] = "Template:Navbox characters FFII",
        ["Final Fantasy III"] = "Template:Navbox characters FFIII",
        ["Final Fantasy IV"] = "Template:Navbox characters FFIV",
        ["Final Fantasy V"] = "Template:Navbox characters FFV",
        ["Final Fantasy VI"] = "Template:Navbox characters FFVI",
        ["Final Fantasy VII"] = "Template:Navbox characters FFVII",
        ["Final Fantasy VIII"] = "Template:Navbox characters FFVIII",
        ["Final Fantasy IX"] = "Template:Navbox characters FFIX",
        ["Final Fantasy X"] = "Template:Navbox characters FFX",
        ["Final Fantasy XI"] = "Template:Navbox characters FFXI",
        ["Final Fantasy XII"] = "Template:Navbox characters FFXII",
        ["Final Fantasy XIII"] = "Template:Navbox characters FFXIII",
        ["Final Fantasy XIV"] = "Template:Navbox characters FFXIV",
        ["Final Fantasy XV"] = "Template:Navbox characters FFXV",
        ["Final Fantasy XVI"] = "Template:Navbox characters FFXVI",
    };

    public async Task ScrapeAsync(CancellationToken ct = default)
    {
        var games = await db.Games.OrderBy(g => g.Id).ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameNavboxes.TryGetValue(game.Name, out var navbox)) continue;

            var roster = await wiki.GetPlayableRosterAsync(navbox, ct);
            if (roster.Count == 0)
            {
                // Final Fantasy's navbox groups its party as "Warriors of Light" and lists the
                // six job classes rather than characters, so it legitimately yields nothing.
                logger.LogWarning("  {Game}: no playable names in {Navbox}", game.Name, navbox);
                continue;
            }

            var characters = await db.Characters.Where(c => c.GameId == game.Id).ToListAsync(ct);
            var byName = new Dictionary<string, Web.Infrastructure.Models.Character>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in characters)
                byName.TryAdd(c.Name, c);

            var matched = 0;
            var missing = new List<string>();

            foreach (var name in roster)
            {
                if (byName.TryGetValue(name, out var character))
                {
                    character.IsPlayable = true;
                    matched++;
                }
                else
                {
                    missing.Add(name);
                }
            }

            // Rows that stopped being listed — a navbox edit, or a name the character scraper
            // has since renamed. Cleared rather than left set, so the flag always describes the
            // current navbox instead of accumulating everything that was ever playable.
            foreach (var character in characters)
            {
                if (character.IsPlayable && !roster.Contains(character.Name, StringComparer.OrdinalIgnoreCase))
                    character.IsPlayable = false;
            }

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "  {Game}: {Matched}/{Total} playable matched{Missing}",
                game.Name, matched, roster.Count,
                missing.Count == 0 ? "" : $" — no row for {string.Join(", ", missing)}");
        }

        logger.LogInformation("Playable rosters done.");
    }
}
