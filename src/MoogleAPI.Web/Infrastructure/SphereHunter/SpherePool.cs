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

    /// <summary>
    /// How many of each game's best-known monsters may be drafted.
    /// </summary>
    /// <remarks>
    /// Per game rather than a single popularity threshold across the library, and the difference
    /// matters. Notability in the battle pool has a median of 60 — requiring health and art already
    /// selects for well-written articles — so the useful thresholds bunch around the middle, and a
    /// global line falls very unevenly: at 60 Final Fantasy XII keeps 362 monsters and Final
    /// Fantasy III keeps 7, and at 65 the third game is down to a single one. The draft deals one
    /// sphere per game, so the thinnest game is what binds, and a global line either guts it or is
    /// too low to do anything.
    /// <para>
    /// Taking each game's top 40 makes every bestiary equally deep by construction. It also drops
    /// the poor picks for the right reason: a deep game's fortieth-best is genuinely notable, so
    /// Final Fantasy XII's Axebeak falls out while Final Fantasy III's Bahamut stays. A flat floor
    /// could not do that — Axebeak scores 58 and Final Fantasy IV's Dark Knight scores 55.
    /// </para>
    /// </remarks>
    public const int DraftablePerGame = 40;

    /// <summary>
    /// The spheres a player may be offered, as opposed to the ones they may meet.
    /// </summary>
    /// <remarks>
    /// Opponents are deliberately not filtered this way, on either count. Dull to pilot and fine to
    /// fight are different tests, a bestiary needs its rank and file, and restricting opponents to
    /// the notable ones would turn every floor into a boss rush.
    /// </remarks>
    public async Task<IReadOnlyList<Sphere>> DraftableAsync(CancellationToken ct) =>
        Draftable(await GetAsync(ct));

    internal static IReadOnlyList<Sphere> Draftable(IEnumerable<Sphere> all) =>
    [
        .. all
            .Where(s => s.Moves.Count >= MinDraftableMoves)
            .GroupBy(s => s.GameId)
            .SelectMany(game => game
                .OrderByDescending(s => s.Popularity)
                // Ties broken by id so the pool is the same list on every request. Whole bestiaries
                // share a notability score, and fortieth place is usually inside such a tie.
                .ThenBy(s => s.Id)
                .Take(DraftablePerGame))
    ];
}
