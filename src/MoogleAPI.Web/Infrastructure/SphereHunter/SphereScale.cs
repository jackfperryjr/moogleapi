using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// Puts every monster in the library on one scale, by asking where it stands among the monsters of
/// its own game rather than what its numbers happen to say.
/// </summary>
/// <remarks>
/// This is the change the whole game rests on. Final Fantasy has no shared scale — a Final Fantasy
/// Goblin has 8 HP and a Final Fantasy XV Bomb has 5,600 — and everything else in this project
/// worked around that: <see cref="BattleMath"/> takes damage as a share of the defender's own
/// maximum HP so that fights last a sane length in every game, and <see cref="BattlePool"/> refuses
/// to stage a fight between two games at all.
/// <para>
/// Both workarounds cost something. Share-of-max-HP means <b>absolute HP cancels out</b> — raising
/// it raises incoming damage by exactly as much — so bulk is decorative and the attack-to-defence
/// ratio is the only real lever. And a party is far less interesting when all three of its members
/// have to come from one bestiary.
/// </para>
/// <para>
/// A percentile fixes both, and it fixes the heavy tail for free: rank does not care that Final
/// Fantasy X runs from 696 HP to 2,000,000, only that Penance is at the top. That tail is precisely
/// what made reading HP off a percentile impossible for Battle Square's champions — see
/// <c>ChampionBuilder</c> — and it is harmless here, because this maps a real monster's real stat
/// onto the curve rather than inventing a stat from a position on it.
/// </para>
/// </remarks>
public sealed class SphereScale
{
    /// <summary>
    /// The bottom of the rating band. Not zero, and not one: a rating is a divisor in the damage
    /// formula, and the weakest monster in a game must be feeble rather than a hole in the maths.
    /// At 10 against a ceiling of 100 the widest possible mismatch is ten-to-one, which is a rout
    /// and not a division-by-nothing.
    /// </summary>
    public const int MinRating = 10;

    public const int MaxRating = 100;

    /// <summary>
    /// Combat HP per point of health rating, so a median monster takes a sensible number of hits.
    /// </summary>
    /// <remarks>
    /// Tuned against the damage formula rather than chosen: at level 50 a neutral power-60 move
    /// between two median monsters deals about 28, and a median health rating of 55 gives 143
    /// health — a little over five hits. The band that produces runs from roughly two hits for the
    /// frailest thing in a bestiary to fifteen for the bulkiest, which is the spread that makes
    /// bulk worth drafting for. Measured against the real bestiary: 2,959 monsters rate a median
    /// health of 143, and near-parity matchups run a median of four turns and a p90 of nine.
    /// </remarks>
    public const double HealthPerRating = 2.6;

    private readonly Dictionary<int, Distribution> _byGame;

    private SphereScale(Dictionary<int, Distribution> byGame) => _byGame = byGame;

    /// <summary>
    /// One game's published numbers for each stat, sorted, so a value can be located in them.
    /// </summary>
    private sealed record Distribution(
        int[] HitPoints, int[] Attack, int[] Defense,
        int[] MagicAttack, int[] MagicDefense, int[] Speed);

    public static SphereScale Build(IReadOnlyList<Fighter> pool)
    {
        var byGame = pool
            .GroupBy(f => f.GameId)
            .ToDictionary(
                game => game.Key,
                game =>
                {
                    var members = game.ToList();
                    return new Distribution(
                        Sorted(members, f => f.HitPoints),
                        Sorted(members, f => f.Attack),
                        Sorted(members, f => f.Defense),
                        Sorted(members, f => f.MagicAttack),
                        Sorted(members, f => f.MagicDefense),
                        Sorted(members, f => f.Speed));
                });

        return new SphereScale(byGame);
    }

    /// <summary>
    /// The six ratings for one monster. A game absent from the pool rates everything at the middle
    /// of the band — it cannot be located against a distribution that does not exist, and the
    /// middle is the answer that distorts a matchup least.
    /// </summary>
    public Ratings For(Fighter fighter)
    {
        if (!_byGame.TryGetValue(fighter.GameId, out var game))
        {
            const int middle = (MinRating + MaxRating) / 2;
            return new Ratings(middle, middle, middle, middle, middle, middle);
        }

        return new Ratings(
            Rate(game.HitPoints, fighter.HitPoints),
            Rate(game.Attack, fighter.Attack),
            Rate(game.Defense, fighter.Defense),
            Rate(game.MagicAttack, fighter.MagicAttack),
            Rate(game.MagicDefense, fighter.MagicDefense),
            Rate(game.Speed, fighter.Speed));
    }

    public record Ratings(
        int HitPoints, int Attack, int Defense, int MagicAttack, int MagicDefense, int Speed)
    {
        /// <summary>What the health rating is worth as an actual pool of hit points.</summary>
        public int MaxHealth => Math.Max(1, (int)Math.Round(HitPoints * HealthPerRating));
    }

    /// <summary>Where a value sits in its game's distribution, as a rating in the band.</summary>
    internal static int Rate(int[] sorted, int value)
    {
        if (sorted.Length == 0) return (MinRating + MaxRating) / 2;

        var percentile = Percentile(sorted, value);
        return (int)Math.Round(MinRating + percentile * (MaxRating - MinRating));
    }

    /// <summary>
    /// The share of the distribution this value stands above, counting ties as half.
    /// </summary>
    /// <remarks>
    /// Midrank rather than "how many are strictly below". Whole bestiaries share a value —
    /// <see cref="BattlePool.FillGapsWithGameMedians"/> fills every missing stat with the game's
    /// median, so in a game that publishes little the median is held by hundreds of monsters at
    /// once. Counting strictly-below puts all of them at the bottom of the band and counting
    /// at-or-below puts all of them at the top; splitting the tie puts them in the middle, which
    /// is where a monster of exactly average strength belongs.
    /// </remarks>
    internal static double Percentile(int[] sorted, int value)
    {
        var below = LowerBound(sorted, value);
        var atOrBelow = UpperBound(sorted, value);

        return (below + atOrBelow) / 2.0 / sorted.Length;
    }

    /// <summary>Index of the first element not less than <paramref name="value"/>.</summary>
    private static int LowerBound(int[] sorted, int value)
    {
        int low = 0, high = sorted.Length;

        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] < value) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    /// <summary>Index of the first element greater than <paramref name="value"/>.</summary>
    private static int UpperBound(int[] sorted, int value)
    {
        int low = 0, high = sorted.Length;

        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] <= value) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    private static int[] Sorted(IReadOnlyList<Fighter> pool, Func<Fighter, int> stat)
    {
        var values = new int[pool.Count];
        for (var i = 0; i < pool.Count; i++) values[i] = Math.Max(1, stat(pool[i]));

        Array.Sort(values);
        return values;
    }
}
