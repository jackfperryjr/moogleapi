using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>Physical moves are answered by Defense, Magic by Magic Defense.</summary>
public enum MoveCategory { Physical, Magic }

/// <summary>
/// The conditions a fight can inflict. One at a time, per the series and per the model.
/// </summary>
/// <remarks>
/// The three that were here before — Poison, Blind, Silence — were chosen because they change what
/// a turn is worth <em>without taking it away</em>, and Sleep and Paralyze were excluded outright
/// because combat had no randomness: a turn-skip that never rolls does not sometimes skip a turn,
/// it skips every turn it is up, and a chain of those is the game playing itself.
/// <para>
/// Seeded rolls remove that objection, so the full set is here. Sleep and Paralyze are still the
/// two to watch in balancing — they are the only ones that can take a turn away, and the reason
/// Sleep is capped at three turns and Paralyze only skips a quarter of the time.
/// </para>
/// </remarks>
public enum Status
{
    None,

    /// <summary>Bio. Bleeds a share of maximum health that grows each turn it stays up.</summary>
    Poison,

    /// <summary>Bleeds a flat share of maximum health and halves physical attack.</summary>
    Sap,

    /// <summary>A quarter of turns are lost outright, and speed is halved.</summary>
    Paralyze,

    /// <summary>Cannot act at all, for one to three turns.</summary>
    Sleep,

    /// <summary>Darkness. Physical moves land for a fraction of their damage.</summary>
    Blind,

    /// <summary>Magic is locked out. The basic attack is physical, so a silenced sphere can act.</summary>
    Silence,
}

/// <param name="Power">
/// Base power in the Pokémon sense — a number around 60 for an ordinary move — rather than the
/// multiplier <see cref="Move.Power"/> carries. The two are related by
/// <see cref="SphereMoves.PowerScale"/>, so the tuning that went into the multipliers survives.
/// </param>
/// <param name="Accuracy">Percentage chance to land, 1–100.</param>
/// <param name="MpCost">
/// What casting it takes out of the sphere's pool. Zero for physical moves, so a sphere with an
/// empty pool can always still swing — which is this game's version of Struggle, except that it is
/// just the attack every monster already has.
/// </param>
/// <param name="IsLimit">
/// The once-per-battle move the gauge unlocks. Never misses, costs nothing, and is not offered
/// until the gauge is full.
/// </param>
public record SphereMove(
    string Name,
    Element? Element,
    MoveCategory Category,
    int Power,
    int Accuracy,
    int MpCost,
    Status Status = Status.None,
    double Recoil = 0,
    bool IsLimit = false);

/// <summary>
/// Turns a monster's scraped ability list into the four buttons a player presses.
/// </summary>
/// <remarks>
/// Built on top of <see cref="MoveBuilder"/> rather than beside it. That class already carries a
/// great deal of hard-won knowledge about what a Final Fantasy ability list actually contains —
/// AI-script stage directions like "Flip sprite horizontally", support spells that a damage model
/// reads as attacks, the whole <c>Haste</c>/<c>Hastega</c> suffix family, Final Fantasy II writing
/// one move three different ways — and none of that changed. What changes is what a move is once
/// it has been identified: this adds the accuracy, the cost and the base power the new formula
/// needs.
/// </remarks>
public static class SphereMoves
{
    /// <summary>
    /// Converts <see cref="Move.Power"/>'s multiplier into base power. An ordinary move is 60,
    /// which is the number the damage constants were tuned against.
    /// </summary>
    public const int PowerScale = 60;

    /// <summary>Accuracy of a move with no power at all, before the penalty below.</summary>
    private const int BaseAccuracy = 112;

    /// <summary>
    /// How much accuracy a point of power costs. Strong moves are less reliable, which is what
    /// makes a weak accurate one worth a button — otherwise the list sorts itself and there is no
    /// decision on it.
    /// </summary>
    private const double AccuracyPerPower = 0.25;

    private const int MinAccuracy = 75;
    private const int MaxAccuracy = 100;

    /// <summary>Magic points one point of base power costs to cast.</summary>
    private const double MpPerPower = 1 / 12.0;

    /// <summary>The pool every sphere has, before its magical talent is counted.</summary>
    private const int BaseMagicPoints = 20;

    /// <summary>
    /// Magic points per point of magic-attack rating. A caster gets roughly ten casts of an
    /// ordinary spell and a brawler gets four, which is enough to make "can I afford this?" a
    /// question without making a mage run dry mid-floor.
    /// </summary>
    private const double MagicPointsPerRating = 0.5;

    public static int MagicPointsFor(int magicAttackRating) =>
        BaseMagicPoints + (int)Math.Round(magicAttackRating * MagicPointsPerRating);

    /// <summary>
    /// The sphere's move list: its abilities, converted, with the Limit appended last.
    /// </summary>
    public static IReadOnlyList<SphereMove> For(string? abilities, string name, Element? affinity)
    {
        var moves = MoveBuilder.Build(abilities).Select(Convert).ToList();
        moves.Add(Limit(name, affinity));
        return moves;
    }

    internal static SphereMove Convert(Move move)
    {
        var power = (int)Math.Round(move.Power * PowerScale);
        var category = move.Kind == MoveKind.Magic ? MoveCategory.Magic : MoveCategory.Physical;

        Elements.TryParse(move.Element, out var element);

        return new SphereMove(
            move.Name,
            move.Element is null ? null : element,
            category,
            power,
            AccuracyFor(move, power),
            category == MoveCategory.Magic ? Math.Max(1, (int)Math.Round(power * MpPerPower)) : 0,
            Convert(move.Status),
            move.Recoil);
    }

    /// <summary>
    /// The basic attack and the suicide moves always land; everything else pays for its power.
    /// </summary>
    /// <remarks>
    /// The attack is exempt because it is the move a sphere falls back to with no magic points
    /// left, and a fallback that misses is not one. Self-destruct is exempt because it already
    /// costs half the user's health — charging it an accuracy penalty on top would make it a
    /// button nobody presses, and it is the whole personality of a Bomb.
    /// </remarks>
    private static int AccuracyFor(Move move, int power)
    {
        if (move.Recoil > 0) return MaxAccuracy;
        if (move.Name.Equals("Attack", StringComparison.OrdinalIgnoreCase)) return MaxAccuracy;

        return Math.Clamp((int)Math.Round(BaseAccuracy - power * AccuracyPerPower), MinAccuracy, MaxAccuracy);
    }

    /// <summary>
    /// The gauge's payoff: one enormous, unmissable, free hit, carrying the sphere's own element
    /// so an affinity bonus applies to it.
    /// </summary>
    /// <remarks>
    /// Magic rather than physical, and deliberately so — the two statuses that hurt the most,
    /// Blind and Sap, both attack physical damage, and a Limit that a status can blunt is a
    /// reward the game can take back after it has been earned.
    /// </remarks>
    internal static SphereMove Limit(string name, Element? affinity) =>
        new($"{name}'s Limit", affinity, MoveCategory.Magic, LimitPower, MaxAccuracy, 0, IsLimit: true);

    /// <summary>
    /// About three and a half times an ordinary move. Enough to turn a fight that is being lost,
    /// which is the point of a gauge that fills on damage taken — and not enough to end a healthy
    /// opponent outright, which would make the whole game a race to fill it.
    /// </summary>
    public const int LimitPower = 210;

    private static Status Convert(StatusEffect status) => status switch
    {
        StatusEffect.Poison => Status.Poison,
        StatusEffect.Blind => Status.Blind,
        StatusEffect.Silence => Status.Silence,
        _ => Status.None,
    };
}
