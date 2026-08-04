using Microsoft.Extensions.Caching.Hybrid;

namespace MoogleAPI.Web.Infrastructure.Data;

/// <summary>
/// One tag over every cached read of the catalogue, so a dashboard edit can drop all of them.
/// </summary>
/// <remarks>
/// The cache keys carry their query parameters — page, game, popularity floor — so a character
/// appears under dozens of them and there is no way to name the ones a single edit invalidated.
/// A tag sidesteps that. It is deliberately one tag rather than one per resource: games are
/// embedded by name in character and monster responses, and a character edit moves the daily
/// puzzle and battle pools, so almost every write crosses resources anyway. Dropping ten minutes
/// of cache on a hand edit costs a few database round trips; showing the curator's own correction
/// back to them as stale data costs their confidence in the tool.
/// </remarks>
public static class CatalogCache
{
    public const string Tag = "catalog";

    public static readonly string[] Tags = [Tag];

    public static ValueTask InvalidateAsync(HybridCache cache, CancellationToken ct) =>
        cache.RemoveByTagAsync(Tag, ct);
}
