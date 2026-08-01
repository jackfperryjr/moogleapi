using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.Arena;

/// <summary>
/// One game's published stats, sorted, so any percentile of them can be read off.
/// </summary>
/// <remarks>
/// This is the unit converter that lets <see cref="LevelCurve"/> be game-agnostic. The curve
/// says "level 40 stands above 55% of this game's monsters"; this turns that into 640 HP in
/// Final Fantasy VII and 31 in Final Fantasy.
/// <para>
/// Built from the same battle pool the opponents are drawn from, so a character's stats are
/// quoted in the scale of the things they will actually meet — not the whole monster table,
/// most of which is content no rung can serve.
/// </para>
/// </remarks>
public sealed class GameStatScale
{
    private readonly int[] _hitPoints;
    private readonly int[] _attack;
    private readonly int[] _defense;
    private readonly int[] _magicAttack;
    private readonly int[] _magicDefense;
    private readonly int[] _speed;

    private GameStatScale(IReadOnlyList<Fighter> pool)
    {
        _hitPoints = Sorted(pool, f => f.HitPoints);
        _attack = Sorted(pool, f => f.Attack);
        _defense = Sorted(pool, f => f.Defense);
        _magicAttack = Sorted(pool, f => f.MagicAttack);
        _magicDefense = Sorted(pool, f => f.MagicDefense);
        _speed = Sorted(pool, f => f.Speed);
    }

    /// <summary>Null when the game has no battle-ready monsters, which means it has no scale.</summary>
    public static GameStatScale? For(IReadOnlyList<Fighter> gamePool) =>
        gamePool.Count == 0 ? null : new GameStatScale(gamePool);

    public int HitPointsAt(double percentile) => Quantile(_hitPoints, percentile);
    public int AttackAt(double percentile) => Quantile(_attack, percentile);
    public int DefenseAt(double percentile) => Quantile(_defense, percentile);
    public int MagicAttackAt(double percentile) => Quantile(_magicAttack, percentile);
    public int MagicDefenseAt(double percentile) => Quantile(_magicDefense, percentile);
    public int SpeedAt(double percentile) => Quantile(_speed, percentile);

    private static int[] Sorted(IReadOnlyList<Fighter> pool, Func<Fighter, int> stat)
    {
        var values = new int[pool.Count];
        for (var i = 0; i < pool.Count; i++)
            values[i] = Math.Max(1, stat(pool[i]));

        Array.Sort(values);
        return values;
    }

    /// <summary>
    /// Linear interpolation between the two nearest samples, rather than nearest-rank.
    /// </summary>
    /// <remarks>
    /// Nearest-rank would quantise the whole level curve to the number of monsters in the game.
    /// Final Fantasy III has 191 battle-ready enemies, so 99 levels spread over a 0.20–0.95
    /// percentile band would land several levels on the same monster and produce identical
    /// stats — levels that visibly cost the player something and change nothing.
    /// </remarks>
    private static int Quantile(int[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 1;
        if (sorted.Length == 1) return sorted[0];

        var position = Math.Clamp(percentile, 0, 1) * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        var fraction = position - lower;

        return Math.Max(1, (int)Math.Round(sorted[lower] + (sorted[upper] - sorted[lower]) * fraction));
    }
}
