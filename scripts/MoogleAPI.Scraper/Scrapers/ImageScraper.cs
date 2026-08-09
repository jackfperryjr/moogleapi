using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Copies every piece of artwork the API points at into our own bucket.
/// </summary>
/// <remarks>
/// Until this runs, the API serves URLs on somebody else's CDN — which hotlink-blocks any request
/// carrying a Referer, meaning every consumer has to remember to suppress it, and any change
/// on their side breaks every image at once. Once copied, the URLs are ours and stable.
/// <para>
/// Idempotent by construction: a row whose <c>ImageSourceUrl</c> is already set has been
/// copied, so a re-run only picks up what is new or was previously missed. <c>--force</c>
/// re-copies everything, which is how you re-encode the library after changing the size or
/// quality settings.
/// </para>
/// </remarks>
public class ImageScraper(AppDbContext db, ImageStore store, ILogger<ImageScraper> logger)
{
    /// <summary>
    /// Uploads run concurrently, but modestly: the source is someone else's CDN and this walks
    /// several thousand files.
    /// </summary>
    private const int Concurrency = 4;

    public async Task ScrapeAsync(bool force = false, CancellationToken ct = default)
    {
        // Bucket first, so a credential or naming problem is reported even on a run that is
        // going to stop at the next check anyway.
        if (!await store.EnsureBucketAsync(ct)) return;

        if (store.PublicBaseUrl is null)
        {
            logger.LogError(
                "R2_PUBLIC_BASE_URL is not set. Copying without it would repoint the API at a " +
                "bucket nobody can read, so nothing has been changed. Enable public access on " +
                "the {Bucket} bucket (or bind a custom domain) and set the variable.", store.Bucket);
            return;
        }

        await RebaseAsync(ct);

        await CopyAsync("monsters", force, ct,
            () => Pending(db.Monsters, force).Select(m => new Art(m.Id, m.ImageUrl!, m.ImageSourceUrl)).ToListAsync(ct),
            async (id, url, source) =>
            {
                var row = await db.Monsters.FirstAsync(m => m.Id == id, ct);
                row.ImageSourceUrl = source;
                row.ImageUrl = url;
            });

        // Characters are not copied any more. Generated art is their primary artwork, chosen and
        // corrected by hand in the dashboard, and a copy stage exists to put wiki art on a row —
        // which for a character is now never the right answer. A row with no picture serves no
        // picture; the clients draw the placeholder. Monsters and cards are unchanged until that
        // half of the library has been reviewed.

        await CopyAsync("cards", force, ct,
            () => Pending(db.Cards, force).Select(c => new Art(c.Id, c.ImageUrl!, c.ImageSourceUrl)).ToListAsync(ct),
            async (id, url, source) =>
            {
                var row = await db.Cards.FirstAsync(c => c.Id == id, ct);
                row.ImageSourceUrl = source;
                row.ImageUrl = url;
            });

        logger.LogInformation("Images done.");
    }

    /// <summary>
    /// Repoints already-copied rows at the current public origin without touching the bucket.
    /// </summary>
    /// <remarks>
    /// An object's key is derived from its row id, so the URL for anything already copied can
    /// simply be recomputed. That makes moving from the r2.dev domain to a custom one — or
    /// between custom domains later — a database pass of a few seconds rather than a re-upload
    /// of the entire library.
    /// <para>
    /// Each layer is rebased at its own address. This used to recompute every <c>ImageUrl</c> as
    /// the plain <c>{folder}/{id}.webp</c> key regardless of what the row was actually serving, so
    /// the default stage — the one described above as the only harmless one — would have taken all
    /// 5,559 rows of generated art out of service on its next run and put the wiki copies back. It
    /// was never noticed because no domain has moved since the art was generated.
    /// </para>
    /// </remarks>
    private async Task RebaseAsync(CancellationToken ct)
    {
        var moved = 0;

        moved += await RebaseSetAsync(db.Monsters, "monsters", ct);
        moved += await RebaseSetAsync(db.Characters, "characters", ct);
        moved += await RebaseSetAsync(db.Cards, "cards", ct);

        if (moved > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Repointed {Count} already-copied images at {Base}.", moved, store.PublicBaseUrl);
        }
    }

    private async Task<int> RebaseSetAsync<T>(DbSet<T> set, string folder, CancellationToken ct) where T : class
    {
        var copied = await set
            .Where(e => EF.Property<string?>(e, "ImageSourceUrl") != null)
            .ToListAsync(ct);

        var moved = 0;
        foreach (var row in copied)
        {
            var entry = set.Entry(row);
            var id = (int)entry.Property("Id").CurrentValue!;
            var generatedKey = $"gen/{folder}/{id}.webp";

            // The generated column first, so ImageUrl can be compared against a current value.
            // Cards have no such column, hence the lookup rather than a direct Property call.
            if (entry.Metadata.FindProperty("GeneratedImageUrl") is not null)
            {
                var generated = entry.Property("GeneratedImageUrl");
                if (generated.CurrentValue is not null)
                {
                    moved += Repoint(generated, store.PublicUrlFor(generatedKey));
                }
            }

            var served = entry.Property("ImageUrl");
            moved += Repoint(served, store.PublicUrlFor(ServedKey((string?)served.CurrentValue, folder, id)));
        }

        return moved;
    }

    /// <summary>
    /// Which stored object a row is currently serving, so a rebase moves it to that object's new
    /// address rather than to a different picture at a different key.
    /// </summary>
    /// <remarks>
    /// The generated image and the copied original are two files, and a row points at one of them.
    /// This used to answer "the copied original" unconditionally, which meant the next domain move
    /// would have repointed all 5,559 rows of generated art back at wiki copies — from the stage
    /// documented as the harmless one, in a pass that reports itself as "repointed N images".
    /// </remarks>
    internal static string ServedKey(string? currentUrl, string folder, int id)
    {
        var generated = $"gen/{folder}/{id}.webp";

        // Matched on the path and not the whole URL, because the host is the thing that just
        // changed. The leading slash keeps monsters/12.webp from matching gen/monsters/12.webp.
        return currentUrl is not null && currentUrl.Contains($"/{generated}", StringComparison.Ordinal)
            ? generated
            : $"{folder}/{id}.webp";
    }

    /// <summary>Sets a URL property, reporting whether it actually changed.</summary>
    private static int Repoint(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry property, string expected)
    {
        if ((string?)property.CurrentValue == expected) return 0;

        property.CurrentValue = expected;
        return 1;
    }

    private record Art(int Id, string ImageUrl, string? ImageSourceUrl);

    private static IQueryable<T> Pending<T>(DbSet<T> set, bool force) where T : class =>
        force
            ? set.Where(e => EF.Property<string?>(e, "ImageUrl") != null)
            : set.Where(e => EF.Property<string?>(e, "ImageUrl") != null
                             && EF.Property<string?>(e, "ImageSourceUrl") == null);

    private async Task CopyAsync(
        string folder,
        bool force,
        CancellationToken ct,
        Func<Task<List<Art>>> load,
        Func<int, string, string, Task> apply)
    {
        var pending = await load();
        logger.LogInformation("Copying {Count} {Folder} images...", pending.Count, folder);

        var sem = new SemaphoreSlim(Concurrency);
        var copied = new System.Collections.Concurrent.ConcurrentBag<(int Id, string Url, string Source)>();

        await Task.WhenAll(pending.Select(async art =>
        {
            // On a re-copy the original source is the truth; ImageUrl already points at us.
            var source = force && art.ImageSourceUrl is not null ? art.ImageSourceUrl : art.ImageUrl;

            await sem.WaitAsync(ct);
            try
            {
                var key = await store.CopyAsync(source, $"{folder}/{art.Id}.webp", ct);
                if (key is not null) copied.Add((art.Id, store.PublicUrlFor(key), source));
            }
            finally { sem.Release(); }
        }));

        // Applied sequentially: the DbContext is not thread-safe.
        foreach (var (id, url, source) in copied)
            await apply(id, url, source);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("  {Copied} of {Total} {Folder} images copied.", copied.Count, pending.Count, folder);
    }
}
