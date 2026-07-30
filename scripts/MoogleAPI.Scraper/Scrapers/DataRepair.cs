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
    }

    private async Task RepairNamesAsync(CancellationToken ct)
    {
        var damaged = await db.Characters
            .Where(c => c.Name.Contains("  "))
            .ToListAsync(ct);

        logger.LogInformation("Repairing {Count} damaged character names...", damaged.Count);

        // Keyed by game so collision checks respect the (Name, GameId) unique index.
        var byGame = (await db.Characters.ToListAsync(ct))
            .GroupBy(c => c.GameId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase));

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
