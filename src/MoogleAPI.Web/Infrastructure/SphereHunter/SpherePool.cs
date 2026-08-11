using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// Every monster sealed in a sphere, rated against its own bestiary.
/// </summary>
/// <remarks>
/// Built on <see cref="BattlePool"/> so the two games agree on what a battle-ready monster is, and
/// cached whole for the same reason that one is: it is a few thousand small records, and holding
/// them keeps a expedition build to one query rather than one per hunt.
/// </remarks>
public class SpherePool(BattlePool pool, HybridCache cache)
{
    /// <summary>
    /// The fewest moves a sphere can have and still be worth drafting.
    /// </summary>
    /// <remarks>
    /// Every sphere has at least two — the basic attack and its Limit — because both are granted
    /// rather than scraped. A sphere on exactly two has no abilities on its article at all, which
    /// is 548 of the 2,961 in the pool, and it is a bad thing to hand a player: two buttons, one of
    /// which is unavailable until a gauge fills. Three leaves 2,413 to choose from.
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
    /// How many of each game's monsters may be drafted.
    /// </summary>
    /// <remarks>
    /// Per game rather than one line across the library. The draft deals one sphere per game, so
    /// the thinnest bestiary is what binds, and any global cut either guts it or does nothing: a
    /// popularity hunt of 60 leaves Final Fantasy XII with 362 monsters and Final Fantasy III with
    /// 7, and at 65 the third game is down to one. Forty per game makes every bestiary equally deep
    /// by construction.
    /// </remarks>
    public const int DraftablePerGame = 40;

    /// <summary>
    /// How many games a monster's name appears in, which is what makes it iconic.
    /// </summary>
    /// <remarks>
    /// Ranking on this rather than on notability, and the two are much further apart than they
    /// look. Popularity is article length and backlinks, which rewards whatever the wiki wrote most
    /// about — and that is bosses, because bosses get long articles. Ranked on it, 58% of the
    /// draftable pool was bosses and only <b>11%</b> were monsters appearing in five or more games;
    /// it offered Brachioraidos and Doga's Clone while missing Coeurl, Flan, Goblin and Zu
    /// entirely. Recurrence takes that 11% to <b>44%</b>.
    /// <para>
    /// Excluding bosses was the other candidate and it is the wrong tool: it moves iconicity only
    /// from 11% to 15% — swapping obscure bosses for obscure ordinary enemies — while deleting the
    /// most famous monsters in the series, because Bahamut is a boss in all four of its
    /// battle-ready forms, as are Ifrit, Omega and Gilgamesh. Recurrence keeps every one of them
    /// and still brings the boss share down to 20% on its own.
    /// </para>
    /// <para>
    /// Counted across the whole battle pool rather than the draftable part of it. Whether one
    /// particular form of a monster has abilities on its article says nothing about how famous the
    /// monster is.
    /// </para>
    /// </remarks>
    internal static Dictionary<string, int> RecurrenceOf(IEnumerable<Sphere> all) =>
        all.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
           .ToDictionary(g => g.Key, g => g.Select(s => s.GameId).Distinct().Count(),
                         StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The spheres a player may be offered, as opposed to the ones they may meet.
    /// </summary>
    /// <remarks>
    /// Opponents are deliberately not filtered this way, on either count. Dull to pilot and fine to
    /// fight are different tests, a bestiary needs its rank and file, and restricting opponents to
    /// the notable ones would turn every hunt into a boss rush.
    /// </remarks>
    public async Task<IReadOnlyList<Sphere>> DraftableAsync(CancellationToken ct) =>
        Draftable(await GetAsync(ct));

    internal static IReadOnlyList<Sphere> Draftable(IEnumerable<Sphere> all)
    {
        var pool = all as IReadOnlyCollection<Sphere> ?? [.. all];
        var recurrence = RecurrenceOf(pool);

        return
        [
            .. pool
                .Where(s => s.Moves.Count >= MinDraftableMoves)
                .GroupBy(s => s.GameId)
                .SelectMany(game => game
                    .OrderByDescending(s => recurrence.GetValueOrDefault(s.Name, 1))
                    // Notability breaks a tie in recurrence rather than deciding the order. Most
                    // monsters appear in exactly one game, so without it the tail of every game's
                    // forty would be arbitrary.
                    .ThenByDescending(s => s.Popularity)
                    // And id breaks that, so the pool is the same list on every request — whole
                    // bestiaries share a notability score, and fortieth place is usually in a tie.
                    .ThenBy(s => s.Id)
                    .Take(DraftablePerGame))
        ];
    }
}
