using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Takes generated art back out of service and discards it.
/// </summary>
/// <remarks>
/// The inverse of <c>--only=promote</c>, and the two steps have to happen together. Promotion
/// points <c>ImageUrl</c> at the generated file, so reverting is not a matter of clearing a
/// column — the served URL has to be pointed back at the copied original first, or every one of
/// those rows serves a broken image the moment the object goes.
/// <para>
/// The deletion is the half that makes the decision stick. Keys derive from the row id, so a
/// generated image sits at a fixed address, and the generate stage adopts anything already
/// there rather than paying to make it again. Clear the column but leave the file and the next
/// batch records the very picture that was just rejected, without generating anything.
/// </para>
/// </remarks>
public class ImageReverter(AppDbContext db, ImageStore store, ILogger<ImageReverter> logger)
{
    /// <param name="only">
    /// Confines the revert to named rows. Null reverts everything holding generated art, which is
    /// what this stage did before the parameter existed — and is almost never what a run wants
    /// now that the library is mostly right. See <see cref="IdSelection"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RevertAsync(IdSelection? only = null, CancellationToken ct = default)
    {
        var monsters = await db.Monsters
            .Where(m => m.GeneratedImageUrl != null
                        && (only == null || only.Monsters.Contains(m.Id)))
            .ToListAsync(ct);

        var characters = await db.Characters
            .Where(c => c.GeneratedImageUrl != null
                        && (only == null || only.Characters.Contains(c.Id)))
            .ToListAsync(ct);

        if (only is not null)
        {
            // A row named but not reverted is worth saying out loud: it means the id was wrong, or
            // the art was already withdrawn. Either way the caller's list and the bucket disagree.
            var missing = only.Count - monsters.Count - characters.Count;
            logger.LogInformation(
                "Restricted to {Named} named rows; {Found} hold generated art{Missing}.",
                only.Count, monsters.Count + characters.Count,
                missing > 0 ? $", {missing} do not" : "");
        }

        logger.LogInformation(
            "Reverting {Monsters} monster and {Characters} character images.", monsters.Count, characters.Count);

        var reverted = new List<string>();
        var stranded = 0;

        foreach (var m in monsters)
        {
            var original = await OriginalFor("monsters", m.Id, m.ImageSourceUrl, ct);
            if (original is null) { stranded++; continue; }

            m.ImageUrl = original;
            m.GeneratedImageUrl = null;
            reverted.Add($"gen/monsters/{m.Id}.webp");
        }

        foreach (var c in characters)
        {
            var original = await OriginalFor("characters", c.Id, c.ImageSourceUrl, ct);
            if (original is null) { stranded++; continue; }

            c.ImageUrl = original;
            c.GeneratedImageUrl = null;
            reverted.Add($"gen/characters/{c.Id}.webp");
        }

        // The database first, and only then the bucket. In this order a failure part-way leaves
        // rows serving their original art with a generated file still sitting behind them —
        // untidy, and fixed by running this again. The other order takes the pictures away while
        // the API is still pointing at them.
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reverted {Count} rows to their original artwork.", reverted.Count);

        if (stranded > 0)
        {
            logger.LogWarning(
                "Left {Count} rows on their generated art — no original copy and no source URL to fall back to. "
                + "Their generated files are kept, because deleting them would leave nothing to serve.", stranded);
        }

        var deleted = 0;
        foreach (var key in reverted)
        {
            if (await store.DeleteAsync(key, ct)) deleted++;
        }

        logger.LogInformation(
            "Deleted {Deleted} of {Total} generated images. The rows are now candidates again.",
            deleted, reverted.Count);
    }

    /// <summary>
    /// Where a row's artwork should point once the generated version is withdrawn.
    /// </summary>
    /// <remarks>
    /// The copy the image stage made of the original, which promotion never touched — it only
    /// re-pointed the column, so the file is still at its own address. The recorded source URL
    /// is the fallback, and it is a poor one: that CDN blocks any request carrying a Referer,
    /// which is the whole reason the images were copied here. Better than a row with no picture.
    /// Null means neither exists, and the caller must leave the row alone.
    /// </remarks>
    private async Task<string?> OriginalFor(string folder, int id, string? sourceUrl, CancellationToken ct)
    {
        var key = $"{folder}/{id}.webp";

        if (await store.ExistsAsync(key, ct)) return store.PublicUrlFor(key);

        return string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl;
    }
}
