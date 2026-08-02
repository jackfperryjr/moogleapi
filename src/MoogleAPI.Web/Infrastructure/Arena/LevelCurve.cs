namespace MoogleAPI.Web.Infrastructure.Arena;

/// <summary>
/// Turns a level into a position in its own game's stat distribution.
/// </summary>
/// <remarks>
/// The problem this exists to solve is that the series has no shared scale. A Final Fantasy
/// Goblin has 8 HP and a Final Fantasy XV Bomb has 5,600, and the same spread runs through
/// every other stat, so there is no table of numbers that means "level 40" in more than one
/// game. Reproducing sixteen games' real growth formulas would fix that and is not worth doing:
/// the wiki does not publish them in any consistent form, and the combat model here could not
/// use the difference if it had them.
/// <para>
/// So a level is stored as a <em>percentile</em> instead. Level 40 places a character above the
/// same share of their game's monsters everywhere, and each game's own numbers supply the
/// units. What a level buys is a position among the things it will be fighting, which is the
/// only thing the fight is decided on.
/// </para>
/// <para>
/// This is worth being explicit about, because it is easy to reach for the wrong lever:
/// <see cref="Battle.BattleMath.DamagePerHit"/> takes damage as a share of the defender's own
/// maximum HP, so raising a character's HP raises the damage they take by exactly as much.
/// Absolute HP cancels out of both fight length and survivability, and the only thing a level
/// can actually move is the attack-to-defence ratio. HP is still computed — the arena carries
/// damage between waves as a fraction, and a number has to be shown — but it is not the dial.
/// </para>
/// </remarks>
public static class LevelCurve
{
    public const int MinLevel = 1;
    public const int MaxLevel = 99;

    /// <summary>
    /// Where level 1 sits among the game's monsters. Above the floor rather than at it: a
    /// character starting below every enemy in the game reads as broken rather than as weak.
    /// </summary>
    private const double MinPercentile = 0.20;

    /// <summary>
    /// Where level 99 sits. Short of 1.0 on purpose — the top of a game's monster distribution
    /// is its superboss, and a level cap that matches Ozma exactly leaves the last rungs with
    /// nothing left to threaten the player.
    /// </summary>
    private const double MaxPercentile = 0.95;

    /// <summary>
    /// Front-loads the curve, the way the series' own do: the early levels are worth much more
    /// than the late ones. Without it, levels 1–30 are nearly indistinguishable and the
    /// recommended-level search has no resolution in the range it usually lands in.
    /// </summary>
    private const double Shape = 0.75;

    /// <summary>
    /// How far along the curve a level sits, from 0 at level 1 to 1 at level 99.
    /// </summary>
    /// <remarks>
    /// Exposed separately because health does not use a percentile — see
    /// <see cref="ChampionBuilder"/> for why — but still has to grow on the same curve, so that
    /// a level buys a consistent share of everything it buys.
    /// </remarks>
    public static double ProgressFor(int level)
    {
        var clamped = Math.Clamp(level, MinLevel, MaxLevel);

        return Math.Pow((clamped - MinLevel) / (double)(MaxLevel - MinLevel), Shape);
    }

    /// <summary>The share of a game's monsters a character at this level stands above.</summary>
    public static double PercentileFor(int level) =>
        MinPercentile + (MaxPercentile - MinPercentile) * ProgressFor(level);
}
