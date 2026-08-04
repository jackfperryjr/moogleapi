using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Images;

namespace MoogleAPI.Web.Features.Dashboard.UploadImage;

public class UploadImageRequest
{
    /// <summary>"characters" or "monsters" — the same words the bucket uses for its folders.</summary>
    public string Resource { get; set; } = string.Empty;

    public int Id { get; set; }

    /// <summary>Which column to point at the new file: "original" or "generated".</summary>
    [QueryParam] public string Slot { get; set; } = "original";

    public IFormFile File { get; set; } = null!;
}

public record UploadImageResponse(int Id, string Slot, string Key, string Url);

/// <summary>
/// Replaces one row's artwork with a hand-picked file.
/// </summary>
/// <remarks>
/// <para>
/// The upload lands on the key the scraper would have used — <c>characters/12.webp</c>, or
/// <c>gen/characters/12.webp</c> for the generated slot — rather than a fresh one per upload, and
/// that is deliberate. The scraper decides what to re-copy by comparing a row's URL against the
/// address it expects that row's art to live at; parking a curated picture anywhere else would
/// leave the row looking uncopied, and the next image run would fetch the wiki's version straight
/// back over the top. Writing to the canonical key makes the curated file simply <em>be</em> that
/// row's art, for every stage that follows.
/// </para>
/// <para>
/// The same reasoning sets <c>ImageSourceUrl</c>. Its normal value is where the picture was
/// fetched from, and the copy stage reads "not null" as "already ours" — so a hand-upload records
/// its own provenance there to stay skipped, rather than leaving a null that invites the scraper
/// to overwrite it.
/// </para>
/// <para>
/// One caveat that cannot be fixed from inside the API: a key that is overwritten keeps its
/// address, so Cloudflare and any browser that already fetched the old picture will hold it until
/// their TTL runs out. The dashboard cache-busts its own preview, so the curator sees the new art
/// at once; the public CDN copy lags.
/// </para>
/// </remarks>
public class Endpoint(AppDbContext db, HybridCache cache, ImageUploadStore store)
    : Endpoint<UploadImageRequest, UploadImageResponse>
{
    /// <summary>Comfortably above any source art, far below anything that would stall the server.</summary>
    private const long MaxBytes = 15 * 1024 * 1024;

    private static readonly string[] Resources = ["characters", "monsters"];
    private static readonly string[] Slots = ["original", "generated"];

    public override void Configure()
    {
        Post("/dashboard/{resource}/{id}/image");
        Policies("Dashboard");
        AllowFileUploads();
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(UploadImageRequest req, CancellationToken ct)
    {
        if (!store.IsConfigured)
        {
            AddError("The image bucket is not configured on this server — set R2_ACCOUNT_ID, " +
                     "ACCESS_KEY, SECRET_KEY and R2_PUBLIC_BASE_URL.");
            await Send.ErrorsAsync(StatusCodes.Status503ServiceUnavailable, ct);
            return;
        }

        var resource = req.Resource.ToLowerInvariant();
        var slot = req.Slot.ToLowerInvariant();

        if (!Resources.Contains(resource) || !Slots.Contains(slot))
        {
            AddError($"Resource must be one of {string.Join(", ", Resources)} and slot one of {string.Join(", ", Slots)}.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        if (req.File.Length == 0 || req.File.Length > MaxBytes)
        {
            AddError(r => r.File, $"File must be between 1 byte and {MaxBytes / (1024 * 1024)} MB.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var exists = resource == "characters"
            ? await db.Characters.AnyAsync(c => c.Id == req.Id, ct)
            : await db.Monsters.AnyAsync(m => m.Id == req.Id, ct);

        if (!exists)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var key = slot == "generated" ? $"gen/{resource}/{req.Id}.webp" : $"{resource}/{req.Id}.webp";

        string url;
        try
        {
            await using var content = req.File.OpenReadStream();
            url = await store.UploadAsync(key, content, ct);
        }
        catch (InvalidImageException ex)
        {
            AddError(r => r.File, $"That file is not an image this server can read ({ex.Message}).");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var provenance = $"uploaded via dashboard {DateTime.UtcNow:yyyy-MM-dd}";

        if (resource == "characters")
        {
            var row = await db.Characters.FirstAsync(c => c.Id == req.Id, ct);
            if (slot == "generated") row.GeneratedImageUrl = url;
            else { row.ImageUrl = url; row.ImageSourceUrl = provenance; }
        }
        else
        {
            var row = await db.Monsters.FirstAsync(m => m.Id == req.Id, ct);
            if (slot == "generated") row.GeneratedImageUrl = url;
            else { row.ImageUrl = url; row.ImageSourceUrl = provenance; }
        }

        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new UploadImageResponse(req.Id, slot, key, url), ct);
    }
}
