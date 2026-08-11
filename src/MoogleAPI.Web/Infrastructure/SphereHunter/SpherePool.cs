using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// Every monster sealed in a sphere, rated against its own bestiary.
/// </summary>
/// <remarks>
/// Built on <see cref="BattlePool"/> so the two games agree on what a battle-ready monster is, and
/// cached whole for the same reason that one is: it is a few thousand small records, and holding
/// them keeps a tower build to one query rather than one per floor.
/// </remarks>
public class SpherePool(BattlePool pool, HybridCache cache)
{
    /// <summary>
    /// The fewest moves a sphere can have and still be worth drafting.
    /// </summary>
    /// <remarks>
    /// Every sphere has at least two — the basic attack and its Limit — because both are granted
    /// rather than scraped. A sphere on exactly two has no abilities on its article at all, which
    /// is 685 of the 2,959 in the pool, and it is a bad thing to hand a player: two buttons, one of
    /// which is unavailable until a gauge fills. Three leaves 2,274 to choose from.
    /// <para>
    /// Opponents are not filtered this way. A two-button monster is dull to pilot and perfectly
    /// fine to fight, and excluding them would thin some bestiaries badly.
    /// </para>
    /// </remarks>
    public const int MinDraftableMoves = 3;

    public async Task<IReadOnlyList<Sphere>> GetAsync(CancellationToken ct)
    {
        var fighters = await pool.GetAsync(ct);

        return await cache.GetOrCreateAsync(
            "spherehunter:pool:v1",
            async _ =>
            {
                var scale = SphereScale.Build(fighters);
                return fighters.Select(f => SphereFactory.Seal(f, scale)).ToList();
            },
            cancellationToken: ct) ?? [];
    }

    /// <summary>The spheres a player may be offered, as opposed to the ones they may meet.</summary>
    public async Task<IReadOnlyList<Sphere>> DraftableAsync(CancellationToken ct) =>
        [.. (await GetAsync(ct)).Where(s => s.Moves.Count >= MinDraftableMoves)];
}
