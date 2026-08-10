using FastEndpoints;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Net.Http.Headers;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.DailySilhouette;

/// <summary>
/// Serves the day's character as a flat black shape — the picture Kupodle's frame holds while the
/// puzzle is unsolved.
/// </summary>
/// <remarks>
/// It returns the bytes rather than the URL, and that is the entire reason it exists. Object keys
/// in this project are derived from the row id, so <c>gen-silhouette/characters/471.webp</c> names
/// the answer outright — and the game client has already bulk-loaded the whole pool with its ids,
/// so a URL in the DOM would be a one-line solve for anyone who opened the network tab. Proxying
/// costs one small image of origin bandwidth a day per player and gives the client nothing it can
/// work backwards from.
/// <para>
/// Everything else here is public already: <c>/characters/daily</c> returns past and present
/// answers outright, by design, so players can catch up on puzzles they missed. This endpoint is
/// not the boundary — it just refuses to widen the leak into the page the game is played on.
/// </para>
/// </remarks>
public class Endpoint(DailyCharacterSelector selector, HybridCache cache, IHttpClientFactory clients)
    : Endpoint<DailySilhouetteRequest>
{
    /// <summary>
    /// A missing silhouette is cached too, as an empty body. The pool is drawn in batches and a
    /// character can legitimately be in it before its shape has been paid for; without this, that
    /// character's day would put a database query and a bucket miss behind every page load.
    /// </summary>
    private static readonly byte[] None = [];

    public override void Configure()
    {
        Get("/characters/daily/silhouette");
        AllowAnonymous();
        Description(b => b
            .WithName("GetDailyCharacterSilhouette")
            .WithSummary("Get the day's character as a silhouette, without naming them")
            .WithTags("Characters")
            .Produces(200, contentType: "image/webp")
            .Produces(404));
    }

    public override async Task HandleAsync(DailySilhouetteRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DailyPuzzle.Today;
        var filters = new PuzzleFilters(req.GameId, req.MinPopularity, req.RequireImage);

        var bytes = await cache.GetOrCreateAsync(
            $"characters:daily:silhouette:{date:yyyy-MM-dd}:{filters.Scope}",
            async token =>
            {
                var character = await selector.SelectAsync(date, filters, token);
                if (character?.SilhouetteImageUrl is not { } url) return None;

                try
                {
                    return await clients.CreateClient("images").GetByteArrayAsync(url, token);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Never cached as a 404: the bucket being briefly unreachable is not the same
                    // fact as this character having no silhouette, and caching it as one would
                    // leave the frame empty for the rest of the entry's lifetime.
                    Logger.LogWarning(ex, "Could not fetch a silhouette from the bucket.");
                    throw;
                }
            },
            tags: CatalogCache.Tags,
            cancellationToken: ct);

        if (bytes.Length == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Safe to cache hard despite the answer changing daily, because the date is in the URL:
        // tomorrow is a different request, not a stale one. An hour is the compromise with the
        // other direction — a silhouette redrawn after a bad result should not take a day to
        // appear.
        HttpContext.Response.GetTypedHeaders().CacheControl =
            new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(1) };

        await Send.BytesAsync(bytes, contentType: "image/webp", cancellation: ct);
    }
}
