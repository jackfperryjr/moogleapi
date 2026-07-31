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
    /// The attack-to-defence ratio is clamped so no matchup is a formality. Articles publish a
    /// defence of 0 or an attack ten times the game's norm often enough that an unclamped
    /// ratio ends fights in a single turn — a Final Fantasy IV Ifrit has defence 5 while a
    /// Deathmask in the same game has 131.
    /// </summary>
    public const double MinRatio = 0.2;
    public const double MaxRatio = 0.8;

    public static double Ratio(int offence, int guard) =>
        Math.Clamp(offence / (double)Math.Max(1, offence + guard), MinRatio, MaxRatio);

    /// <summary>Damage one use of a move deals. Zero when the defender absorbs its element.</summary>
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
