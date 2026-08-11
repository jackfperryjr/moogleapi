using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Infrastructure.Battle;

/// <summary>
/// Every monster that can fight, across the games that have the stats to support one.
/// </summary>
/// <remarks>
/// Shared by both battle games. Kupo Climb draws its opponents from it and Battle Square draws
/// both its waves and its per-game stat scale, so the two have to agree on what "the pool" is:
/// a character's level is quoted as a percentile of these numbers, and vetting a wave against a
/// different set than the one it was measured against would make the level mean nothing.
/// </remarks>
public class BattlePool(AppDbContext db, HybridCache cache)
{
    /// <summary>
    /// The games a battle can actually take place in. Battles never cross games, so a fight is
    /// only playable when both sides come from the same one.
    /// </summary>
    /// <remarks>
    /// Absent for want of stats: VIII, whose enemy articles give HP as level-scaling
    /// coefficients rather than a number, and XI, XIV and XVI, which publish almost none.
    /// <para>
    /// II is absent for a different reason. It has HP and art for 166 monsters, so it looks
    /// playable — but exactly one of them has a listed elemental weakness, against 31–91% in
    /// every other game here. Elemental choice is the whole decision in a battle, so a fight in
    /// II is a damage race with nothing to think about.
    /// </para>
    /// </remarks>
    public static readonly int[] GameIds = [1, 3, 4, 5, 6, 7, 9, 10, 12, 13, 15];

    /// <summary>
    /// Loaded whole and cached: it is a few thousand small rows, and holding it in memory keeps
    /// a full run build to one query instead of one per wave.
    /// </summary>
    public async Task<List<Fighter>> GetAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            "battle:pool:v2",
            async token =>
            {
                var raw = await db.Monsters
                    .Where(m => GameIds.Contains(m.GameId)
                                && m.HitPoints != null && m.HitPoints > 0
                                && m.ImageUrl != null
                                // Content that was never a real encounter. The wiki documents
                                // cut and debug entries as enemies — Final Fantasy VI's
                                // "Unnamed cutscene" is a dummied cutscene loader with 1 HP —
                                // and they're valid data, just not something to fight. They
                                // stay in the API and are excluded only from the battle pool.
                                && !m.Name.StartsWith("Unnamed")
                                && (m.Description == null
                                    || (!m.Description.Contains("dummied")
                                        && !m.Description.Contains("unused")
                                        && !m.Description.Contains("is a debug"))))
                    .OrderBy(m => m.Id)
                    .Select(m => new RawFighter(
                        m.Id, m.Name, m.GameId, m.Game.Name, m.Category, m.HitPoints!.Value,
                        m.Attack, m.Defense, m.MagicAttack, m.MagicDefense, m.Speed,
                        m.Weaknesses, m.Absorbs, m.Abilities, m.ImageUrl, m.Popularity))
                    .ToListAsync(token);

                return FillGapsWithGameMedians(raw);
            },
            cancellationToken: ct) ?? [];

    /// <summary>
    /// Articles omit individual stats constantly — Final Fantasy II publishes no magic attack
    /// at all — so a monster missing one is still a fine opponent and shouldn't be excluded.
    /// What it can't have is a flat placeholder: a fixed default of 10 sits wildly above or
    /// below whatever scale the game actually uses, and a fight between one monster's real
    /// stat and another's placeholder becomes a blowout. An FFII Ogre Chief with a defaulted
    /// magic attack of 10 hit a Bomb's genuine magic defence of 4 for a third of its health a
    /// turn. Filling from the game's own median keeps both sides on the same scale.
    /// </summary>
    internal static List<Fighter> FillGapsWithGameMedians(List<RawFighter> raw)
    {
        var fighters = new List<Fighter>(raw.Count);

        foreach (var game in raw.GroupBy(r => r.GameId))
        {
            // A stat is only ever compared against its opposite, so when a game publishes none
            // of one it borrows the other's scale rather than a constant. Final Fantasy II
            // lists no magic attack whatsoever but does list magic defence in single digits —
            // pairing them keeps that exchange even, where a flat 10 against a real 4 let every
            // FFII enemy hit for a third of the player's health per turn.
            var attack = Median(game, r => r.Attack, r => r.Defense);
            var defense = Median(game, r => r.Defense, r => r.Attack);
            var magicAttack = Median(game, r => r.MagicAttack, r => r.MagicDefense);
            var magicDefense = Median(game, r => r.MagicDefense, r => r.MagicAttack);
            var speed = Median(game, r => r.Speed, r => r.Speed);

            var defenseScale = Commensurate(attack, defense);
            var magicDefenseScale = Commensurate(magicAttack, magicDefense);

            fighters.AddRange(game.Select(r => new Fighter(
                r.Id, r.Name, r.GameId, r.GameName, r.Category, r.HitPoints,
                r.Attack ?? attack, Rescale(r.Defense ?? defense, defenseScale),
                r.MagicAttack ?? magicAttack, Rescale(r.MagicDefense ?? magicDefense, magicDefenseScale),
                r.Speed ?? speed,
                r.Weaknesses, r.Absorbs, r.Abilities, r.ImageUrl, r.Popularity)));
        }

        return fighters.OrderBy(f => f.Id).ToList();
    }

    /// <summary>
    /// What to multiply a game's guard stat by to put it on the same scale as the offence it is
    /// measured against, so that the median pairing in every game sits at parity.
    /// </summary>
    /// <remarks>
    /// <see cref="BattleMath.Ratio"/> divides offence by guard, which assumes the two are
    /// comparable numbers. Across this series they frequently aren't, because they are not the
    /// same stat in each game. Final Fantasy XV's articles give strength a median of 4,080 and
    /// vitality a median of 107, so a median enemy reads as having a forty-to-one advantage over
    /// another median enemy and kills it in two turns. Final Fantasy VI runs the other way — a
    /// small attack against a 0–255 defence — and pinned every fight at the
    /// <see cref="BattleMath.MinRatio"/> floor for seventeen turns.
    /// <para>
    /// Scaling is monotonic, so it changes nothing about which monsters are tougher than which
    /// within a game; it only fixes where the pair as a whole sits. A game that publishes no
    /// guard stat at all already borrows the offence median, which makes this factor exactly 1
    /// and leaves that behaviour untouched.
    /// </para>
    /// </remarks>
    internal static double Commensurate(int offenceMedian, int guardMedian) =>
        guardMedian <= 0 ? 1 : offenceMedian / (double)guardMedian;

    private static int Rescale(int value, double factor) =>
        Math.Max(1, (int)Math.Round(value * factor));

    /// <summary>
    /// Median of the values a game publishes for a stat, falling back to its opposing stat's
    /// median, and to 10 only when the game publishes neither.
    /// </summary>
    private static int Median(IEnumerable<RawFighter> game, Func<RawFighter, int?> stat, Func<RawFighter, int?> opposite)
    {
        var fighters = game as IReadOnlyCollection<RawFighter> ?? game.ToList();

        return MedianOf(fighters, stat) ?? MedianOf(fighters, opposite) ?? 10;
    }

    private static int? MedianOf(IEnumerable<RawFighter> game, Func<RawFighter, int?> stat)
    {
        var values = game.Select(stat).Where(v => v is > 0).Select(v => v!.Value).OrderBy(v => v).ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }

    internal record RawFighter(
        int Id, string Name, int GameId, string GameName, string? Category, int HitPoints,
        int? Attack, int? Defense, int? MagicAttack, int? MagicDefense, int? Speed,
        string? Weaknesses, string? Absorbs, string? Abilities, string? ImageUrl, int Popularity);
}
