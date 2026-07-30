using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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

    /// <param name="force">
    /// Overwrite existing field values instead of only filling nulls. Needed because
    /// earlier scraper versions stored unparsed infobox fragments (e.g. Role = "|race=Android"),
    /// and the null-coalescing merge below can never repair a non-null junk value.
    /// </param>
    public async Task ScrapeAsync(bool force = false, CancellationToken ct = default)
    {
        var games = await db.Games.ToListAsync(ct);

        foreach (var game in games)
        {
            if (!GameCategories.TryGetValue(game.Name, out var category)) continue;

            logger.LogInformation("Scraping characters for {Game}...", game.Name);

            var members = await wiki.GetCategoryMembersAsync(category, ct);

            var candidates = members
                .Where(m => !m.Title.Contains('/') &&
                            !m.Title.Contains(" characters", StringComparison.OrdinalIgnoreCase) &&
                            !m.Title.Contains(" enemies", StringComparison.OrdinalIgnoreCase))
                .Select(m => (Member: m, Name: NormalizeName(m.Title)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            logger.LogInformation("  Found {Count} candidates", candidates.Count);

            // Pre-load existing characters; TryAdd handles any duplicate names in the DB.
            var existing = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in await db.Characters.Where(c => c.GameId == game.Id).ToListAsync(ct))
                existing.TryAdd(c.Name, c);

            // Fetch wiki data for new/incomplete characters — 2 concurrent
            var sem = new SemaphoreSlim(2);
            var detailsMap = new ConcurrentDictionary<string, CharacterDetails>();
            var signalsMap = new ConcurrentDictionary<string, PageSignals>();

            await Task.WhenAll(candidates.Select(async item =>
            {
                if (!force && existing.TryGetValue(item.Name, out var ch) && !NeedsEnrichment(ch))
                    return;

                await sem.WaitAsync(ct);
                try
                {
                    detailsMap[item.Name] = await wiki.GetCharacterDetailsAsync(item.Member.Title, ct);

                    var signals = await wiki.GetPageSignalsAsync(item.Member.Title, ct);
                    if (signals is not null)
                        signalsMap[item.Name] = signals;
                }
                finally { sem.Release(); }
            }));

            // Apply results sequentially (DbContext is not thread-safe)
            foreach (var (_, name) in candidates)
            {
                if (!detailsMap.TryGetValue(name, out var details)) continue;

                signalsMap.TryGetValue(name, out var signals);

                if (!existing.TryGetValue(name, out var ch))
                {
                    db.Characters.Add(new Character
                    {
                        Name           = name,
                        Description    = details.Description,
                        Role           = details.Role,
                        Affiliation    = details.Affiliation,
                        Race           = details.Race,
                        Hometown       = details.Hometown,
                        ImageUrl       = details.ImageUrl,
                        GameId         = game.Id,
                        WikiPageLength = signals?.PageLength,
                        WikiBacklinks  = signals?.Backlinks,
                        Popularity     = ScorePopularity(signals)
                    });
                    logger.LogInformation("  + {Name}", name);
                }
                else if (force)
                {
                    // Only overwrite when the fresh parse actually produced something —
                    // a failed parse shouldn't wipe good existing data.
                    ch.Description  = details.Description ?? ch.Description;
                    ch.Role         = details.Role        ?? ch.Role;
                    ch.Affiliation  = details.Affiliation ?? ch.Affiliation;
                    ch.Race         = details.Race        ?? ch.Race;
                    ch.Hometown     = details.Hometown    ?? ch.Hometown;
                    ch.ImageUrl     = details.ImageUrl    ?? ch.ImageUrl;
                    if (signals is not null)
                    {
                        ch.WikiPageLength = signals.PageLength;
                        ch.WikiBacklinks  = signals.Backlinks;
                        ch.Popularity     = ScorePopularity(signals);
                    }
                    logger.LogInformation("  * refreshed {Name}", name);
                }
                else
                {
                    ch.Description  ??= details.Description;
                    ch.Role         ??= details.Role;
                    ch.Affiliation  ??= details.Affiliation;
                    ch.Race         ??= details.Race;
                    ch.Hometown     ??= details.Hometown;
                    ch.ImageUrl     ??= details.ImageUrl;
                    if (signals is not null)
                    {
                        ch.WikiPageLength = signals.PageLength;
                        ch.WikiBacklinks  = signals.Backlinks;
                        ch.Popularity     = ScorePopularity(signals);
                    }
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation("  ~ enriched {Name}", name);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Characters done.");
    }

    private static readonly Regex TrailingParenthetical = new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);
    private static readonly Regex RepeatedWhitespace = new(@"\s{2,}", RegexOptions.Compiled);

    // Strips disambiguation parentheticals: "Auron (Final Fantasy X party member)" → "Auron"
    private static string NormalizeName(string title) =>
        RepeatedWhitespace.Replace(TrailingParenthetical.Replace(title, ""), " ").Trim();

    private static bool NeedsEnrichment(Character c) =>
        c.Description is null || c.ImageUrl is null || c.Role is null || c.WikiPageLength is null;

    // Article size and inbound links both span orders of magnitude, so score on a log
    // scale and blend. Bounds are calibrated against observed extremes: ~40 bytes / 0 links
    // for a walk-on NPC, ~120k bytes / 500+ links for a series lead.
    private const double MinLogLength = 1.5;   // ~32 bytes
    private const double MaxLogLength = 5.0;   // ~100k bytes
    private const int    BacklinkCap  = 500;   // matches the API's lhlimit

    internal static int ScorePopularity(PageSignals? signals)
    {
        if (signals is null) return 0;

        var lengthScore = Normalize(Math.Log10(signals.PageLength + 1), MinLogLength, MaxLogLength);
        var linkScore   = Math.Log10(Math.Min(signals.Backlinks, BacklinkCap) + 1) / Math.Log10(BacklinkCap + 1);

        return (int)Math.Round(Math.Clamp(lengthScore * 0.6 + linkScore * 0.4, 0, 1) * 100);
    }

    private static double Normalize(double value, double min, double max) =>
        Math.Clamp((value - min) / (max - min), 0, 1);
}
