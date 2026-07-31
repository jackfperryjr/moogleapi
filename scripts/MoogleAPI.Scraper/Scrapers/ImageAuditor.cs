using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Records what each stored image actually is, so later passes can select a batch by query.
/// </summary>
/// <remarks>
/// Split out from generation deliberately. The first version classified inline, which meant
/// downloading all 5,643 images to find the 317 worth regenerating — thousands of pointless
/// requests, and a burst big enough that the CDN started refusing TLS handshakes. Classifying
/// once and storing the answer turns that into a <c>WHERE</c> clause.
/// </remarks>
public class ImageAuditor(AppDbContext db, ILogger<ImageAuditor> logger)
{
    private const int Concurrency = 4;

    public async Task AuditAsync(bool force = false, CancellationToken ct = default)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        // Cloudflare's bot rules reject the default .NET agent on the images domain.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MoogleAPI-Scraper/1.0)");

        await AuditSetAsync(db.Monsters, "monsters", http, force, ct);
        await AuditSetAsync(db.Characters, "characters", http, force, ct);

        logger.LogInformation("Image audit done.");
    }

    private async Task AuditSetAsync<T>(DbSet<T> set, string label, HttpClient http, bool force, CancellationToken ct)
        where T : class
    {
        var rows = await set
            .Where(e => EF.Property<string?>(e, "ImageUrl") != null
                        && (force || EF.Property<string?>(e, "ImageKind") == null))
            .Select(e => new { Id = EF.Property<int>(e, "Id"), Url = EF.Property<string>(e, "ImageUrl") })
            .ToListAsync(ct);

        logger.LogInformation("Classifying {Count} {Label} images...", rows.Count, label);

        var verdicts = new System.Collections.Concurrent.ConcurrentBag<(int Id, string Kind)>();
        var sem = new SemaphoreSlim(Concurrency);
        var failed = 0;

        await Task.WhenAll(rows.Select(async row =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var bytes = await DownloadAsync(http, row.Url, ct);
                if (bytes is null) { Interlocked.Increment(ref failed); return; }

                using var image = Image.Load<Rgba32>(bytes);
                verdicts.Add((row.Id, ImageClassifier.Classify(image).ToString()));
            }
            catch (ImageFormatException)
            {
                Interlocked.Increment(ref failed);
            }
            finally { sem.Release(); }
        }));

        foreach (var (id, kind) in verdicts)
        {
            var entity = await set.FindAsync([id], ct);
            if (entity is not null) set.Entry(entity).Property("ImageKind").CurrentValue = kind;
        }

        await db.SaveChangesAsync(ct);

        foreach (var group in verdicts.GroupBy(v => v.Kind).OrderByDescending(g => g.Count()))
            logger.LogInformation("  {Kind,-16} {Count}", group.Key, group.Count());

        if (failed > 0) logger.LogWarning("  {Count} {Label} images could not be read.", failed, label);
    }

    /// <summary>
    /// A burst of parallel requests occasionally fails the TLS handshake outright rather than
    /// returning a status, so a transient failure is retried before the row is given up on.
    /// </summary>
    private static async Task<byte[]?> DownloadAsync(HttpClient http, string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await http.GetByteArrayAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt == 2) return null;
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }

        return null;
    }
}
