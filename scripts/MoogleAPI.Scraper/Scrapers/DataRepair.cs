using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// One-time cleanup of rows written by earlier scraper versions. Two defects are fixed:
/// names that kept a game-numeral suffix after the game title was stripped out of the
/// middle ("Noctis  XV party member"), and infobox fragments stored as field values
/// ("|race=Android"). Both are invisible to the normal scrape — the name mismatch makes
/// the row unreachable, and non-null junk is never revisited.
/// </summary>
public class DataRepair(AppDbContext db, ILogger<DataRepair> logger)
{
    // Two-or-more spaces followed by a Final Fantasy numeral and anything after it.
    private static readonly Regex GameNumeralSuffix =
        new(@"\s{2,}(?:IX|IV|XI{0,3}|XIV|XV|XVI|VI{0,3}|V|I{1,3}|X)\b.*$", RegexOptions.Compiled);

    private static readonly Regex RepeatedWhitespace = new(@"\s{2,}", RegexOptions.Compiled);

    // A value that still carries wikitext template syntax was never parsed properly.
    // Field names may contain spaces ("|japanese voice actor ="), hence the space in the class.
    private static readonly Regex UnparsedInfobox = new(@"\|\s*[a-zA-Z_][a-zA-Z_ ]*\s*=", RegexOptions.Compiled);

    public async Task RepairAsync(CancellationToken ct = default)
    {
        await RepairNamesAsync(ct);
        await ScrubFieldsAsync(ct);
        await PurgeNonMonsterRowsAsync(ct);
        await RepairMonsterNamesAsync(ct);
    }

    /// <summary>
    /// Removes rows that were never monsters. An early scrape swept the enemy categories'
    /// reference pages into the table alongside the enemies themselves — ability pages
    /// ("Flare  enemy ability"), enemy-family pages ("Dragon  enemy type"), and per-game
    /// index pages ("Final Fantasy II enemies") — and it also created a row for every
    /// redirect in the category, which is most of what the FFXIV and FFXVI categories hold.
    /// </summary>
    private async Task PurgeNonMonsterRowsAsync(CancellationToken ct)
    {
        var candidates = await db.Monsters
            .Select(m => new { m.Id, m.Name, m.Description, m.ImageUrl, m.HitPoints, m.WikiPageLength })
            .ToListAsync(ct);

        var junkNames = candidates.Where(m => IsNotAMonster(m.Name)).Select(m => m.Id).ToHashSet();

        // A redirect fetches successfully but yields nothing, so it can't be told apart by
        // a failed request — only by the article being tiny and empty of everything we store.
        var stubs = candidates
            .Where(m => m.WikiPageLength is > 0 and < StubPageLength
                        && m.Description is null && m.ImageUrl is null && m.HitPoints is null)
            .Select(m => m.Id)
            .ToHashSet();

        var doomed = junkNames.Union(stubs).ToList();
        if (doomed.Count == 0)
        {
            logger.LogInformation("No non-monster rows to purge.");
            return;
        }

        logger.LogInformation(
            "Purging {Total} non-monster rows ({Junk} reference pages, {Stubs} redirect stubs)...",
            doomed.Count, junkNames.Count, stubs.Count);

        // Chunked: a single IN clause with ten thousand ids risks Npgsql's parameter ceiling.
        foreach (var batch in doomed.Chunk(1000))
        {
            var ids = batch.ToList();
            var deleted = await db.Monsters.Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(ct);
            logger.LogInformation("  purged {Count} rows", deleted);
        }
    }

    // A redirect's wikitext is one line; the shortest real enemy articles run several hundred bytes.
    private const int StubPageLength = 200;

    private static readonly Regex NotAMonster = new(
        @"^enem(y|ies)$" +
        @"|\b(enem(y|ies)?\s*\(?\s*(abilit(y|ies)|types?|famil(y|ies)|actions?|formations|stats|data)|enemies|bestiary|list of)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True for reference and index pages that describe enemies rather than being one.</summary>
    public static bool IsNotAMonster(string name) => NotAMonster.IsMatch(name);

    /// <summary>
    /// Repairs monster names damaged the same way character names were: the disambiguating
    /// parenthetical was stripped out of the middle of the title, leaving its tail stranded
    /// behind a double space ("Lamia (Final Fantasy IV)" → "Lamia  IV"). Until the name is
    /// repaired the row matches no wiki article, so no scrape can ever reach it.
    /// </summary>
    private async Task RepairMonsterNamesAsync(CancellationToken ct)
    {
        var damaged = await db.Monsters
            .Where(m => m.Name.Contains("  ") || m.Name.Contains("("))
            .ToListAsync(ct);

        logger.LogInformation("Repairing {Count} damaged monster names...", damaged.Count);

        // Keyed by game so collision checks respect the (Name, GameId) unique index. That
        // index is case-sensitive in Postgres but the lookup is not, so a game can hold two
        // rows this dictionary considers the same key — TryAdd keeps the first rather than
        // throwing, and the loser is left for a later pass.
        var byGame = new Dictionary<int, Dictionary<string, Monster>>();
        foreach (var m in await db.Monsters.ToListAsync(ct))
        {
            if (!byGame.TryGetValue(m.GameId, out var siblings))
                byGame[m.GameId] = siblings = new Dictionary<string, Monster>(StringComparer.OrdinalIgnoreCase);
            siblings.TryAdd(m.Name, m);
        }

        var removed = 0;
        var renamed = 0;

        foreach (var monster in damaged)
        {
            var oldName  = monster.Name;
            var repaired = RepairMonsterName(oldName);
            if (repaired.Length == 0 || repaired.Equals(oldName, StringComparison.Ordinal))
                continue;

            var siblings = byGame[monster.GameId];
            siblings.Remove(oldName);

            if (siblings.TryGetValue(repaired, out var incumbent))
            {
                // Both rows describe the same monster. Keep whichever is more complete.
                if (Completeness(monster) > Completeness(incumbent))
                {
                    db.Monsters.Remove(incumbent);
                    monster.Name = repaired;
                    siblings[repaired] = monster;
                }
                else
                {
                    db.Monsters.Remove(monster);
                }

                removed++;
                continue;
            }

            monster.Name = repaired;
            siblings[repaired] = monster;
            renamed++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Monster names repaired: {Renamed} renamed, {Removed} duplicates merged.", renamed, removed);
    }

    // An unclosed parenthetical is the same damage seen from the other side: the closing
    // half of "Borghen (Final Fantasy II boss)" went with the stripped game title.
    private static readonly Regex UnclosedParenthetical = new(@"\s*\([^)]*$", RegexOptions.Compiled);

    // What's left of a disambiguator once the game title is gone: "Chaos  boss", "Bomb  creature".
    private static readonly Regex DisambiguatorResidue =
        new(@"\s{2,}(boss|enemy|creature|ability|type|character|party member)\b.*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>"Lamia  IV" → "Lamia", "Emperor (final boss" → "Emperor". Idempotent.</summary>
    public static string RepairMonsterName(string name)
    {
        name = UnclosedParenthetical.Replace(name, "");
        name = GameNumeralSuffix.Replace(name, "");
        name = DisambiguatorResidue.Replace(name, "");
        return RepeatedWhitespace.Replace(name, " ").Trim();
    }

    private static int Completeness(Monster m) =>
        (m.Description is null ? 0 : 1) + (m.ImageUrl is null ? 0 : 1) +
        (m.Location is null ? 0 : 1) + (m.HitPoints is null ? 0 : 1) +
        (m.Weaknesses is null ? 0 : 1) + (m.Category is null ? 0 : 1);

    private async Task RepairNamesAsync(CancellationToken ct)
    {
        var damaged = await db.Characters
            .Where(c => c.Name.Contains("  "))
            .ToListAsync(ct);

        logger.LogInformation("Repairing {Count} damaged character names...", damaged.Count);

        // Keyed by game so collision checks respect the (Name, GameId) unique index. See the
        // monster pass below for why this can't be a plain ToDictionary.
        var byGame = new Dictionary<int, Dictionary<string, Character>>();
        foreach (var c in await db.Characters.ToListAsync(ct))
        {
            if (!byGame.TryGetValue(c.GameId, out var siblings))
                byGame[c.GameId] = siblings = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            siblings.TryAdd(c.Name, c);
        }

        var removed = 0;

        foreach (var ch in damaged)
        {
            var oldName  = ch.Name;
            var repaired = RepairName(oldName);
            if (repaired.Length == 0 || repaired.Equals(oldName, StringComparison.Ordinal))
                continue;

            var siblings = byGame[ch.GameId];
            siblings.Remove(oldName);

            if (siblings.TryGetValue(repaired, out var incumbent))
            {
                // Both rows describe the same character. Keep whichever is more complete.
                if (Completeness(ch) > Completeness(incumbent))
                {
                    db.Characters.Remove(incumbent);
                    ch.Name = repaired;
                    siblings[repaired] = ch;
                }
                else
                {
                    db.Characters.Remove(ch);
                }

                removed++;
                logger.LogInformation("  merged duplicate: {Old} → {New}", oldName, repaired);
                continue;
            }

            ch.Name = repaired;
            siblings[repaired] = ch;
            logger.LogInformation("  {Old} → {New}", oldName, repaired);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Names repaired. {Removed} duplicates merged.", removed);
    }

    private async Task ScrubFieldsAsync(CancellationToken ct)
    {
        var all = await db.Characters.ToListAsync(ct);
        var scrubbed = 0;

        foreach (var c in all)
        {
            var before = (c.Role, c.Affiliation, c.Race, c.Hometown);

            c.Role        = Clean(c.Role);
            c.Affiliation = Clean(c.Affiliation);
            c.Race        = Clean(c.Race);
            c.Hometown    = Clean(c.Hometown);

            if (before != (c.Role, c.Affiliation, c.Race, c.Hometown))
                scrubbed++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Scrubbed unparsed infobox fragments from {Count} characters.", scrubbed);
    }

    /// <summary>"Noctis  XV party member" → "Noctis". Idempotent on already-clean names.</summary>
    public static string RepairName(string name) =>
        RepeatedWhitespace.Replace(GameNumeralSuffix.Replace(name, ""), " ").Trim();

    /// <summary>Nulls a value that still carries wikitext, so the next scrape refetches it.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (UnparsedInfobox.IsMatch(value)) return null;
        if (value.StartsWith('|') || value.Contains("{{") || value.Contains("[[")) return null;
        return value.Trim();
    }

    private static int Completeness(Character c) =>
        (c.Description is null ? 0 : 1) + (c.Role is null ? 0 : 1) +
        (c.Affiliation is null ? 0 : 1) + (c.Race is null ? 0 : 1) +
        (c.Hometown is null ? 0 : 1) + (c.ImageUrl is null ? 0 : 1);
}
