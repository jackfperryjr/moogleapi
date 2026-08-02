namespace MoogleAPI.Web.Infrastructure.Battle;

/// <summary>
/// The combat model, in one place.
/// </summary>
/// <remarks>
/// Battles resolve in the browser, but the rules live here and ship inside the run payload.
/// That matters because the server uses this same arithmetic to decide whether a matchup is
/// winnable: if the client kept its own copy of the constants, the two would drift and the
/// vetting would quietly stop describing the fight the player actually gets.
/// </remarks>
public static class BattleMath
{
    /// <summary>
    /// Damage is a share of the defender's own maximum HP rather than a flat number. The
    /// series' scales are incompatible — a Final Fantasy Goblin has 8 HP where a Final Fantasy
    /// XV Bomb has 5,600 — so a flat formula makes early games one-shot exchanges and later
    /// ones a grind. Scaling by max HP gives every rung a fight of about the same length.
    /// </summary>
    public const double DamageShare = 0.30;

    public const double WeaknessMultiplier = 2.0;

    /// <summary>
    /// Floor on the attack-to-defence ratio, so nothing is ever unhittable. Articles publish a
    /// defence of 0 or an attack ten times the game's norm often enough that an unguarded
    /// formula produces absurdities in both directions.
    /// </summary>
    public const double MinRatio = 0.2;

    /// <summary>
    /// Ceiling on it. High enough that overwhelming force actually overwhelms: at
    /// <see cref="DamageShare"/> 0.30 a ratio above about 2.6 kills in one hit.
    /// </summary>
    /// <remarks>
    /// This was 0.8, which combined with <see cref="DamageShare"/> put a hard floor of roughly
    /// four turns on <em>every</em> fight in the game, no matter how lopsided. A level 99 Tidus
    /// with 211 attack needed four turns to kill a 110 HP Killer Bee, because the ceiling threw
    /// away all but a sliver of a forty-to-one advantage. That is defensible when both sides are
    /// monsters of a comparable tier, which is all Kupo Climb ever stages — but Battle Square
    /// deliberately opens on enemies far beneath the player, and a model with no concept of a
    /// rout reads as broken rather than as balanced.
    /// </remarks>
    public const double MaxRatio = 4.0;

    /// <summary>
    /// The ratio at parity, and so the coefficient on the curve below.
    /// </summary>
    /// <remarks>
    /// Chosen to hold the old behaviour exactly where the old behaviour was right. An evenly
    /// matched pair scored 0.5 under the previous formula and scores 0.5 here, and the two stay
    /// within a few percent of each other out to about a two-to-one advantage. They only diverge
    /// where the old one had stopped responding at all.
    /// </remarks>
    public const double RatioScale = 0.5;

    /// <summary>Share of maximum HP that Poison bleeds after the afflicted combatant acts.</summary>
    public const double PoisonShare = 0.06;

    /// <summary>What a Physical move is worth while its user is Blind.</summary>
    public const double BlindMultiplier = 0.5;

    /// <summary>
    /// How long a status lasts, counted in the afflicted combatant's own turns. Long enough to
    /// change the shape of a fight, short enough that landing one is worth doing again.
    /// </summary>
    public const int StatusTurns = 3;

    /// <summary>
    /// How much of its nominal damage a move keeps, given the attacker's offence against the
    /// defender's guard.
    /// </summary>
    /// <remarks>
    /// The square root of the advantage rather than <c>offence / (offence + guard)</c>. That
    /// expression cannot exceed 1 however large the advantage grows — a thousand-to-one edge
    /// scores 0.999, the same as a four-to-one edge — so past a certain point extra attack
    /// bought nothing at all. A root keeps rising, gently: four times the attack is twice the
    /// damage, and it takes a forty-to-one advantage to kill in a single hit.
    /// </remarks>
    public static double Ratio(int offence, int guard) =>
        Math.Clamp(RatioScale * Math.Sqrt(offence / (double)Math.Max(1, guard)), MinRatio, MaxRatio);

    /// <summary>Damage one use of a move deals. Zero when the defender absorbs its element.</summary>
    /// <remarks>
    /// Statuses are deliberately not modelled here. This arithmetic exists to vet a matchup as
    /// winnable, and it should describe the fight at its worst rather than its best: an estimate
    /// that counted the player's poison would rate matchups as fair that are only fair if a
    /// particular button lands, while one that counted the enemy's blind would reject rungs that
    /// play fine. Ignoring both keeps the estimate conservative in the direction that matters.
    /// </remarks>
    public static double DamagePerHit(Fighter attacker, Fighter defender, Move move)
    {
        if (move.Element is not null && Lists(defender.Absorbs).Contains(move.Element))
            return 0;

        var offence = move.Kind == MoveKind.Magic ? attacker.MagicAttack : attacker.Attack;
        var guard = move.Kind == MoveKind.Magic ? defender.MagicDefense : defender.Defense;

        var damage = defender.HitPoints * DamageShare * move.Power * Ratio(offence, guard);

        return move.Element is not null && Lists(defender.Weaknesses).Contains(move.Element)
            ? damage * WeaknessMultiplier
            : damage;
    }

    /// <summary>
    /// Turns the attacker needs to finish the defender using its best <em>sustainable</em> move.
    /// Self-destruct is excluded on purpose: it costs half the user's health, so it can win a
    /// fight but can't be the plan for one — counting it would rate hopeless matchups as fair.
    /// </summary>
    public static int TurnsToKill(Fighter attacker, IReadOnlyList<Move> attackerMoves, Fighter defender)
    {
        var best = attackerMoves
            .Where(m => m.Recoil == 0)
            .Select(m => DamagePerHit(attacker, defender, m))
            .DefaultIfEmpty(0)
            .Max();

        return best <= 0 ? int.MaxValue : (int)Math.Ceiling(defender.HitPoints / best);
    }

    private static IEnumerable<string> Lists(string? commaSeparated) =>
        string.IsNullOrWhiteSpace(commaSeparated)
            ? []
            : commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
