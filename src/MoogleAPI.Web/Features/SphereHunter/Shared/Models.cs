using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.Shared;

/// <param name="Affinity">The sphere's own element, or null for a non-elemental one.</param>
/// <param name="Health">
/// Health at the reference level, so two spheres can be compared in the draft. Actual health on a
/// hunt is <c>hitPoints × healthPerRating × level / referenceLevel</c> — the client is given those
/// constants in <see cref="BattleRules"/> rather than a number per hunt.
/// </param>
public record SphereView(
    int Id,
    string Name,
    string GameName,
    string? Category,
    string? ImageUrl,
    string? Affinity,
    int HitPoints,
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Speed,
    int Health,
    int Magic,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Absorbs,
    IReadOnlyList<MoveView> Moves)
{
    public static SphereView Of(Sphere s) => new(
        s.Id, s.Name, s.GameName, s.Category, s.ImageUrl, s.Affinity?.ToString(),
        s.Ratings.HitPoints, s.Ratings.Attack, s.Ratings.Defense,
        s.Ratings.MagicAttack, s.Ratings.MagicDefense, s.Ratings.Speed,
        s.Ratings.HealthAt(SphereScale.ReferenceLevel), s.MaxMagic,
        [.. s.Weaknesses.Select(e => e.ToString())],
        [.. s.Absorbs.Select(e => e.ToString())],
        [.. s.Moves.Select(MoveView.Of)]);
}

public record MoveView(
    string Name, string? Element, string Category, int Power, int Accuracy, int MagicCost,
    string Status, double Recoil, bool IsLimit)
{
    public static MoveView Of(SphereMove m) => new(
        m.Name, m.Element?.ToString(), m.Category.ToString(), m.Power, m.Accuracy, m.MpCost,
        m.Status.ToString(), m.Recoil, m.IsLimit);
}

/// <summary>
/// Every constant the browser needs to resolve a battle the way the server vetted it.
/// </summary>
/// <remarks>
/// Shipped rather than duplicated in the client on purpose, and it is the same arrangement the
/// older games use. The server decides whether a hunt is winnable using this arithmetic; a client
/// carrying its own copy of the numbers drifts from it, and when it does the vetting quietly stops
/// describing the fight the player actually gets.
/// </remarks>
public record BattleRules(
    double HealthPerRating,
    int ReferenceLevel,
    double AffinityBonus,
    double CriticalMultiplier,
    double CriticalChance,
    double MinVariance,
    double MaxVariance,
    double SuperEffective,
    double NotVeryEffective,
    double BlindMultiplier,
    double SapAttackMultiplier,
    double SapShare,
    double PoisonShare,
    double ParalyzeSkipChance,
    double ParalyzeSpeedMultiplier,
    int MinSleepTurns,
    int MaxSleepTurns,
    int StatusTurns,
    int LimitFull,
    double LimitFillRate,
    double RecoveryBetweenHunts,
    int PartySize)
{
    public static BattleRules Current => new(
        SphereScale.HealthPerRating, SphereScale.ReferenceLevel,
        SphereMath.AffinityBonus, SphereMath.CriticalMultiplier, SphereMath.CriticalChance,
        SphereMath.MinVariance, SphereMath.MaxVariance,
        Elements.SuperEffective, Elements.NotVeryEffective,
        SphereMath.BlindMultiplier, SphereMath.SapAttackMultiplier, SphereMath.SapShare,
        SphereMath.PoisonShare, SphereMath.ParalyzeSkipChance, SphereMath.ParalyzeSpeedMultiplier,
        SphereMath.MinSleepTurns, SphereMath.MaxSleepTurns, SphereMath.StatusTurns,
        SphereMath.LimitFull, SphereMath.LimitFillRate,
        HuntBuilder.RecoveryBetweenHunts, HuntBuilder.PartySize);
}
