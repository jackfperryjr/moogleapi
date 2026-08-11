using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// One monster as it fights: its ratings, its element, its moves, and the pools it spends.
/// </summary>
/// <remarks>
/// Distinct from <see cref="Fighter"/>, which carries a monster's published numbers. A sphere
/// carries its <em>ratings</em> — where those numbers place it among its own bestiary — which is
/// what lets a Final Fantasy Goblin and a Final Fantasy XV Bomb stand in the same party.
/// </remarks>
public record Sphere(
    int Id,
    string Name,
    int GameId,
    string GameName,
    string? Category,
    string? ImageUrl,
    Element? Affinity,
    SphereScale.Ratings Ratings,
    int MaxMagic,
    IReadOnlyList<Element> Weaknesses,
    IReadOnlyList<Element> Absorbs,
    IReadOnlyList<SphereMove> Moves)
{
    public bool IsBoss => Category == "Boss";

    /// <summary>
    /// The sphere's health on a given floor. A function of level rather than a stored number,
    /// because damage grows with level and a fixed pool would make the tower get faster as it got
    /// harder — see <see cref="SphereScale.Ratings.HealthAt"/>.
    /// </summary>
    public int HealthAt(int level) => Ratings.HealthAt(level);
}

/// <summary>
/// The combat model for Sphere Hunter, in one place.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> an edit to <see cref="BattleMath"/>. That model is shared with Battle
/// Square, which is balanced around share-of-max-HP damage and a ratio curve that has already been
/// retuned once in play; changing it under that game to suit this one would break a working thing
/// to build a new one. The two coexist.
/// <para>
/// Battles resolve in the browser, but the rules live here and ship inside the run payload — the
/// same arrangement as the old model and for the same reason: the server uses this arithmetic to
/// decide whether a floor is winnable, and a client with its own copy of the constants would drift
/// until the vetting stopped describing the fight the player actually gets.
/// </para>
/// </remarks>
public static class SphereMath
{
    /// <summary>Bonus for using a move of your own element — the series' affinity, Pokémon's STAB.</summary>
    public const double AffinityBonus = 1.5;

    public const double CriticalMultiplier = 1.5;

    /// <summary>One in sixteen, as the source material has it.</summary>
    public const double CriticalChance = 1.0 / 16.0;

    /// <summary>
    /// The damage roll. Never above 1, so the number a player is quoted is the best case rather
    /// than an average they will usually miss.
    /// </summary>
    public const double MinVariance = 0.85;
    public const double MaxVariance = 1.0;

    /// <summary>What a physical move keeps while its user is Blind.</summary>
    public const double BlindMultiplier = 0.5;

    /// <summary>What a physical move keeps while its user is Sapped.</summary>
    public const double SapAttackMultiplier = 0.5;

    /// <summary>Health lost at the end of a turn to Sap, as a share of maximum.</summary>
    public const double SapShare = 0.06;

    /// <summary>
    /// Poison's first tick, as a share of maximum health. It compounds: the second turn costs
    /// twice this, the third three times, so a poison left up becomes the reason to switch.
    /// </summary>
    public const double PoisonShare = 0.04;

    /// <summary>Chance a paralysed sphere loses its turn outright.</summary>
    public const double ParalyzeSkipChance = 0.25;

    public const double ParalyzeSpeedMultiplier = 0.5;

    public const int MinSleepTurns = 1;
    public const int MaxSleepTurns = 3;

    /// <summary>How long the other conditions last, counted in the afflicted sphere's own turns.</summary>
    public const int StatusTurns = 3;

    /// <summary>
    /// Damage before the rolls — what the move is worth on paper.
    /// </summary>
    /// <remarks>
    /// The standard formula, on ratings rather than on published stats. Level comes from the floor,
    /// so the tower is progression: the same move is worth more on floor sixteen than on floor one,
    /// against opponents who have grown by the same curve.
    /// <para>
    /// Note what this does that the old model could not: <b>health is a real stat</b>. Damage no
    /// longer scales with the defender's own maximum, so drafting something bulky actually buys
    /// survival. That is the whole reason for the rating scale — see <see cref="SphereScale"/>.
    /// </para>
    /// </remarks>
    public static double BaseDamage(Sphere attacker, Sphere defender, SphereMove move, int level)
    {
        var offence = move.Category == MoveCategory.Magic
            ? attacker.Ratings.MagicAttack
            : attacker.Ratings.Attack;

        var guard = move.Category == MoveCategory.Magic
            ? defender.Ratings.MagicDefense
            : defender.Ratings.Defense;

        return (2.0 * level / 5 + 2) * move.Power * offence / Math.Max(1, guard) / 50 + 2;
    }

    /// <summary>
    /// What the defender's affinities do to an incoming element.
    /// </summary>
    /// <remarks>
    /// The published data wins outright and the grid only fills its silence. A monster the wiki
    /// says is weak to Ice takes double from Ice, full stop — it is not also multiplied by whatever
    /// the grid thinks of Ice against its inferred element, because that would compound a real fact
    /// with a guess and land on quadruple damage. The grid is consulted only where the article says
    /// nothing at all, which is most pairings, and it is the only source of a
    /// not-very-effective tier.
    /// </remarks>
    public static double Effectiveness(Sphere defender, Element? element)
    {
        if (element is not { } attacking) return Elements.Neutral;

        if (defender.Absorbs.Contains(attacking)) return Elements.Absorbed;
        if (defender.Weaknesses.Contains(attacking)) return Elements.SuperEffective;

        return Elements.Effectiveness(attacking, defender.Affinity);
    }

    /// <summary>Whether the attacker's own element matches the move's.</summary>
    public static double Affinity(Sphere attacker, SphereMove move) =>
        move.Element is not null && move.Element == attacker.Affinity ? AffinityBonus : 1.0;

    /// <summary>
    /// Everything except the rolls: base damage, affinity, effectiveness, and the attacker's own
    /// condition.
    /// </summary>
    public static double Deterministic(
        Sphere attacker, Sphere defender, SphereMove move, int level, Status attackerStatus)
    {
        var damage = BaseDamage(attacker, defender, move, level)
                     * Affinity(attacker, move)
                     * Effectiveness(defender, move.Element);

        if (move.Category != MoveCategory.Physical) return damage;

        if (attackerStatus == Status.Blind) damage *= BlindMultiplier;
        if (attackerStatus == Status.Sap) damage *= SapAttackMultiplier;

        return damage;
    }

    /// <summary>What one use of a move actually does, rolls included.</summary>
    public static Strike Resolve(
        Sphere attacker, Sphere defender, SphereMove move, int level, Status attackerStatus,
        DeterministicRandom rng)
    {
        // Rolled before the hit test so that a miss and a hit consume the same amount of the
        // stream. Otherwise the sequence of every later roll depends on whether an earlier one
        // landed, and a client replaying the battle from the seed would diverge the moment it
        // disagreed about a single miss.
        var accuracyRoll = rng.Next(100);
        var criticalRoll = rng.Next(10_000);
        var varianceRoll = rng.Next(1_000);

        var effectiveness = Effectiveness(defender, move.Element);

        // Absorbing is not a dodge — the move lands and does nothing, so it cannot miss and
        // cannot crit. Reporting it as a miss would tell the player the wrong thing about why.
        if (effectiveness == Elements.Absorbed)
            return new Strike(0, false, false, effectiveness);

        if (accuracyRoll >= move.Accuracy)
            return new Strike(0, true, false, effectiveness);

        var critical = criticalRoll < CriticalChance * 10_000;
        var variance = MinVariance + (MaxVariance - MinVariance) * (varianceRoll / 1_000.0);

        var damage = Deterministic(attacker, defender, move, level, attackerStatus)
                     * (critical ? CriticalMultiplier : 1.0)
                     * variance;

        return new Strike(Math.Max(1, (int)Math.Round(damage)), false, critical, effectiveness);
    }

    /// <param name="Missed">
    /// True only for an accuracy failure. An absorbed move deals zero and did not miss.
    /// </param>
    public record Strike(int Damage, bool Missed, bool Critical, double Effectiveness);

    /// <summary>Health lost at the end of a turn to a lingering condition.</summary>
    /// <param name="turnsHeld">How many turns the condition has already been up, from 1.</param>
    public static int TickDamage(Sphere sphere, Status status, int turnsHeld, int level) => status switch
    {
        Status.Poison => Math.Max(1, (int)Math.Round(sphere.HealthAt(level) * PoisonShare * turnsHeld)),
        Status.Sap => Math.Max(1, (int)Math.Round(sphere.HealthAt(level) * SapShare)),
        _ => 0,
    };

    /// <summary>A full gauge. Kept as a round number because the client draws it as a percentage.</summary>
    public const int LimitFull = 100;

    /// <summary>
    /// How fast the gauge fills, against health lost as a share of maximum.
    /// </summary>
    /// <remarks>
    /// At 1.4 a sphere that has lost roughly seventy per cent of its health has a full gauge, which
    /// is the number that makes switching a real question. Much faster and the Limit is just the
    /// fourth move; much slower and it only ever fires on a sphere about to faint, where it cannot
    /// change the fight it was earned in.
    /// </remarks>
    public const double LimitFillRate = 1.4;

    /// <summary>
    /// Gauge earned by taking a hit. It fills on damage <em>taken</em>, not dealt, so the sphere
    /// carrying the party's damage is the one that earns the payoff — and because the gauge
    /// survives a switch, a battered sphere on the bench is a loaded gun rather than a liability.
    /// </summary>
    public static int LimitGained(Sphere sphere, int damage, int level) =>
        (int)Math.Round(LimitFull * LimitFillRate * damage / Math.Max(1, sphere.HealthAt(level)));

    /// <summary>Effective speed, which decides who moves first.</summary>
    public static int Speed(Sphere sphere, Status status) =>
        status == Status.Paralyze
            ? (int)Math.Round(sphere.Ratings.Speed * ParalyzeSpeedMultiplier)
            : sphere.Ratings.Speed;

    /// <summary>
    /// Turns the attacker needs to finish the defender with its best sustainable move, used to
    /// vet a floor rather than to play one.
    /// </summary>
    /// <remarks>
    /// Deterministic on purpose: no crit, no variance, no status, and accuracy folded in as an
    /// expectation rather than rolled. The estimate should describe the fight at its plainest, and
    /// one that counted a lucky crit would rate matchups as fair that are only fair when the dice
    /// agree.
    /// <para>
    /// Self-destruct is excluded for the reason it always was — it costs half the user's health,
    /// so it can win a fight but cannot be the plan for one. The Limit is excluded because it
    /// fires once and the gauge may never fill.
    /// </para>
    /// </remarks>
    public static int TurnsToKill(Sphere attacker, Sphere defender, int level)
    {
        var best = attacker.Moves
            .Where(m => m.Recoil == 0 && !m.IsLimit)
            .Select(m => Deterministic(attacker, defender, m, level, Status.None) * (m.Accuracy / 100.0))
            .DefaultIfEmpty(0)
            .Max();

        return best <= 0 ? int.MaxValue : (int)Math.Ceiling(defender.HealthAt(level) / best);
    }
}
