using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Fills a monster row from two sources: the per-game enemy category supplies the roster,
/// and each enemy article supplies art, encounter location, battle stats, elemental
/// affinities, and the notability signals. Bosses are told apart by membership of the
/// game's boss category rather than by anything on the article itself.
/// </summary>
public class MonsterScraper(AppDbContext db, WikiClient wiki, ILogger<MonsterScraper> logger)
{
    private static readonly Dictionary<string, string> GameCategories = new()
    {
        ["Final Fantasy"] = "Enemies in Final Fantasy",
        ["Final Fantasy II"] = "Enemies in Final Fantasy II",
        ["Final Fantasy III"] = "Enemies in Final Fantasy III",
        ["Final Fantasy IV"] = "Enemies in Final Fantasy IV",
        ["Final Fantasy V"] = "Enemies in Final Fantasy V",
        ["Final Fantasy VI"] = "Enemies in Final Fantasy VI",
        ["Final Fantasy VII"] = "Enemies in Final Fantasy VII",
        ["Final Fantasy VIII"] = "Enemies in Final Fantasy VIII",
        ["Final Fantasy IX"] = "Enemies in Final Fantasy IX",
        ["Final Fantasy X"] = "Enemies in Final Fantasy X",
        ["Final Fantasy XI"] = "Enemies in Final Fantasy XI",
        ["Final Fantasy XII"] = "Enemies in Final Fantasy XII",
        ["Final Fantasy XIII"] = "Enemies in Final Fantasy XIII",
        ["Final Fantasy XIV"] = "Enemies in Final Fantasy XIV",
        ["Final Fantasy XV"] = "Enemies in Final Fantasy XV",
        ["Final Fantasy XVI"] = "Enemies in Final Fantasy XVI",
    };

    /// <param name="force">
    /// Re-fetch and overwrite existing values instead of only filling in what is missing.
    /// </param>
    public async Task ScrapeAsync(bool force = false, CancellationToken ct = default)
    {
        var games = await db.Games.ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameCategories.TryGetValue(game.Name, out var category)) continue;

            logger.LogInformation("Scraping monsters for {Game}...", game.Name);

            var members = await wiki.GetCategoryMembersAsync(category, ct);

            var candidates = members
                .Where(m => !m.Title.Contains('/') && !IsMetaArticle(m.Title))
                .Select(m => (Member: m, Name: NormalizeName(m.Title)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            logger.LogInformation("  Found {Count} candidates", candidates.Count);

            var bosses = await GetBossNamesAsync(game.Name, ct);
            logger.LogInformation("  {Count} of them are bosses", bosses.Count);

            // Pre-load existing monsters; TryAdd handles any duplicate names in the DB.
            var existing = new Dictionary<string, Monster>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in await db.Monsters.Where(m => m.GameId == game.Id).ToListAsync(ct))
                existing.TryAdd(m.Name, m);

            // Fetch articles for new/incomplete monsters — 2 concurrent
            var sem = new SemaphoreSlim(2);
            var detailsMap = new ConcurrentDictionary<string, MonsterDetails>();

            await Task.WhenAll(candidates.Select(async item =>
            {
                if (!force && existing.TryGetValue(item.Name, out var m) && !NeedsEnrichment(m))
                    return;

                await sem.WaitAsync(ct);
                try
                {
                    detailsMap[item.Name] = await wiki.GetMonsterDetailsAsync(item.Member.Title, ct);
                }
                finally { sem.Release(); }
            }));

            var galleryImages = await ResolveGalleryImagesAsync(detailsMap, ct);

            // Apply results sequentially (DbContext is not thread-safe)
            foreach (var (_, name) in candidates)
            {
                detailsMap.TryGetValue(name, out var details);

                var monsterCategory = bosses.Contains(name) || IsBossType(details?.Type) ? "Boss" : "Enemy";
                var imageUrl = ResolveImageUrl(details, galleryImages);
                var stats = details?.Stats ?? MonsterStats.Empty;

                if (!existing.TryGetValue(name, out var monster))
                {
                    // A monster only enters the table with an article behind it — otherwise
                    // the row would be a bare name with no stats, description, or art.
                    if (details is null || !HasContent(details, stats)) continue;

                    db.Monsters.Add(new Monster
                    {
                        Name = name,
                        Description = details.Description,
                        Category = monsterCategory,
                        Location = details.Location,
                        ImageUrl = imageUrl,
                        HitPoints = stats.HitPoints,
                        MagicPoints = stats.MagicPoints,
                        Level = stats.Level,
                        Experience = stats.Experience,
                        Gil = stats.Gil,
                        Weaknesses = stats.Weaknesses,
                        Absorbs = stats.Absorbs,
                        Attack = stats.Attack,
                        Defense = stats.Defense,
                        MagicAttack = stats.MagicAttack,
                        MagicDefense = stats.MagicDefense,
                        Speed = stats.Speed,
                        Evasion = stats.Evasion,
                        Abilities = stats.Abilities,
                        Drops = stats.Drops,
                        Steals = stats.Steals,
                        GameId = game.Id,
                        WikiPageLength = details.Signals?.PageLength,
                        WikiBacklinks = details.Signals?.Backlinks,
                        Popularity = CharacterScraper.ScorePopularity(details.Signals)
                    });
                    logger.LogInformation("  + {Name}", name);
                    continue;
                }

                // Category needs no article fetch, so it is applied even to rows that were
                // skipped as already complete — that's what backfills it for existing data.
                if (force || monster.Category is null)
                    monster.Category = monsterCategory;

                if (details is null) continue;

                if (force)
                {
                    // Only overwrite when the fresh parse actually produced something —
                    // a failed parse shouldn't wipe good existing data.
                    monster.Description = details.Description ?? monster.Description;
                    monster.Location = details.Location ?? monster.Location;
                    monster.ImageUrl = imageUrl ?? monster.ImageUrl;
                    monster.HitPoints = stats.HitPoints ?? monster.HitPoints;
                    monster.MagicPoints = stats.MagicPoints ?? monster.MagicPoints;
                    monster.Level = stats.Level ?? monster.Level;
                    monster.Experience = stats.Experience ?? monster.Experience;
                    monster.Gil = stats.Gil ?? monster.Gil;
                    monster.Weaknesses = stats.Weaknesses ?? monster.Weaknesses;
                    monster.Absorbs = stats.Absorbs ?? monster.Absorbs;
                    monster.Attack = stats.Attack ?? monster.Attack;
                    monster.Defense = stats.Defense ?? monster.Defense;
                    monster.MagicAttack = stats.MagicAttack ?? monster.MagicAttack;
                    monster.MagicDefense = stats.MagicDefense ?? monster.MagicDefense;
                    monster.Speed = stats.Speed ?? monster.Speed;
                    monster.Evasion = stats.Evasion ?? monster.Evasion;
                    monster.Abilities = stats.Abilities ?? monster.Abilities;
                    monster.Drops = stats.Drops ?? monster.Drops;
                    monster.Steals = stats.Steals ?? monster.Steals;
                    logger.LogInformation("  * refreshed {Name}", name);
                }
                else
                {
                    monster.Description ??= details.Description;
                    monster.Location ??= details.Location;
                    monster.ImageUrl ??= imageUrl;
                    monster.HitPoints ??= stats.HitPoints;
                    monster.MagicPoints ??= stats.MagicPoints;
                    monster.Level ??= stats.Level;
                    monster.Experience ??= stats.Experience;
                    monster.Gil ??= stats.Gil;
                    monster.Weaknesses ??= stats.Weaknesses;
                    monster.Absorbs ??= stats.Absorbs;
                    monster.Attack ??= stats.Attack;
                    monster.Defense ??= stats.Defense;
                    monster.MagicAttack ??= stats.MagicAttack;
                    monster.MagicDefense ??= stats.MagicDefense;
                    monster.Speed ??= stats.Speed;
                    monster.Evasion ??= stats.Evasion;
                    monster.Abilities ??= stats.Abilities;
                    monster.Drops ??= stats.Drops;
                    monster.Steals ??= stats.Steals;
                    logger.LogInformation("  ~ enriched {Name}", name);
                }

                if (details.Signals is not null)
                {
                    monster.WikiPageLength = details.Signals.PageLength;
                    monster.WikiBacklinks = details.Signals.Backlinks;
                    monster.Popularity = CharacterScraper.ScorePopularity(details.Signals);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Monsters done.");
    }

    // The boss category is a flat sibling of the enemy category: "Bosses in Final Fantasy VI".
    // Games without one (FFXI and FFXIV don't keep one) simply leave every monster an Enemy.
    private async Task<HashSet<string>> GetBossNamesAsync(string gameName, CancellationToken ct)
    {
        var titles = await wiki.GetCategoryPageTitlesAsync($"Bosses in {gameName}", ct);

        return titles
            .Where(t => !t.Contains('/'))
            .Select(NormalizeName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // FFV and FFXV label an enemy's kind on the article itself; every other game's "type"
    // is a creature family ("Giant", "Daemon") and leaves the row an Enemy.
    private static bool IsBossType(string? type) =>
        type is not null && type.Equals("Boss", StringComparison.OrdinalIgnoreCase);

    // Sprite-era enemies keep their art in a <gallery>, which MediaWiki doesn't expose as a
    // page image, so those filenames have to be resolved to CDN URLs in a separate batch.
    private async Task<Dictionary<string, string>> ResolveGalleryImagesAsync(
        IReadOnlyDictionary<string, MonsterDetails> details, CancellationToken ct)
    {
        var fileNames = details.Values
            .Where(d => d.ImageUrl is null && d.ImageFileName is not null)
            .Select(d => d.ImageFileName!)
            .ToList();

        if (fileNames.Count == 0) return [];

        logger.LogInformation("  Resolving {Count} gallery images", fileNames.Count);
        return await wiki.ResolveImageUrlsAsync(fileNames, ct);
    }

    private static string? ResolveImageUrl(MonsterDetails? details, Dictionary<string, string> galleryImages)
    {
        if (details?.ImageUrl is not null) return details.ImageUrl;
        if (details?.ImageFileName is null) return null;

        return galleryImages.TryGetValue(details.ImageFileName, out var url) ? url : null;
    }

    // Stats are excluded on purpose: plenty of articles genuinely have no stats infobox, and
    // including them here would re-fetch those pages on every run forever.
    private static bool NeedsEnrichment(Monster m) =>
        m.WikiPageLength is null || m.Description is null || m.ImageUrl is null;

    /// <summary>
    /// The enemy categories also hold the reference pages that describe a game's enemies
    /// collectively, rather than being an enemy. Those have to be excluded by name: they are
    /// the longest, most heavily linked articles in the category, so they score a perfect
    /// notability rating and would sit at the very top of the pool a game draws its answers
    /// from — "Final Fantasy VII enemy abilities" outranking Gilgamesh.
    /// </summary>
    private static readonly Regex MetaArticle = new(
        @"^enem(y|ies)$" +
        @"|\b(enem(y|ies)\s+(abilit(y|ies)|actions?|formations|stats|data|types?|famil(y|ies))|enemies|characters|bestiary|list of)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsMetaArticle(string title) => MetaArticle.IsMatch(title);

    // A redirect fetches successfully and yields a page length, so the only way to tell one
    // from a real article is that nothing worth storing came out of it. Without this check
    // the weekly run recreates every stub the repair pass deletes.
    private static bool HasContent(MonsterDetails details, MonsterStats stats) =>
        details.Description is not null ||
        details.Location is not null ||
        details.ImageUrl is not null ||
        details.ImageFileName is not null ||
        stats != MonsterStats.Empty;

    private static readonly Regex TrailingParenthetical = new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);

    private static string NormalizeName(string title) =>
        TrailingParenthetical.Replace(title, "").Trim();
}
