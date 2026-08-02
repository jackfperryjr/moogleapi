namespace MoogleAPI.Web.Features.Arena.GetRun;

/// <param name="CharacterId">Character to enter, from GET /api/arena/roster.</param>
/// <param name="Level">
/// Optional, 1–99. Defaults to the character's recommended level for the day.
/// </param>
/// <param name="Date">Which day's waves. Defaults to today; future dates are rejected.</param>
public record GetArenaRunRequest(int CharacterId, int? Level, DateOnly? Date);

/// <param name="Element">Fire, Ice, Thunder, Water, Earth, Wind, Holy, Dark — null is non-elemental.</param>
/// <param name="Kind">Physical is reduced by Defense, Magic by MagicDefense.</param>
/// <param name="Status">"Poison", "Blind", "Silence" or "None".</param>
public record MoveOption(string Name, string? Element, string Kind, double Power, double Recoil, string Status);

/// <param name="Weaknesses">Elements this combatant takes double damage from.</param>
/// <param name="Absorbs">Elements that heal it instead of hurting it.</param>
public record Combatant(
    int Id,
    string Name,
    string GameName,
    string? Category,
    int HitPoints,
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Speed,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Absorbs,
    IReadOnlyList<MoveOption> Moves,
    string? ImageUrl
);

/// <param name="Archetype">Warrior, Mage, Scout or Balanced.</param>
public record ChampionEntry(
    int CharacterId,
    string Name,
    string Archetype,
    string? Job,
    string? Weapon,
    int Level,
    int RecommendedLevel,
    Combatant Fighter
);

/// <param name="Kind">
/// What the slots take away: None, HalveHitPoints, Blind, Silence, Poison, SealAbilities,
/// WeakenOffence or StripDefence.
/// </param>
/// <param name="Status">
/// The status to apply for the wave, when the handicap is one — "Poison", "Blind", "Silence"
/// or "None". Sent separately from <paramref name="Kind"/> so a client can apply it without
/// having to know which kinds map to a status.
/// </param>
/// <param name="Multiplier">What clearing the wave under this handicap is worth.</param>
public record HandicapOption(string Kind, string Name, string Description, string Status, double Multiplier);

/// <param name="Cost">
/// Share of the champion's maximum health the wave is expected to cost, ignoring the handicap.
/// Nothing heals between waves, so these accumulate — which is what the level recommendation is
/// solved against.
/// </param>
public record ArenaWave(
    int Number,
    Combatant Opponent,
    HandicapOption Handicap,
    int BattlePoints,
    double Cost
);

/// <inheritdoc cref="Battle.GetRun.BattleRules"/>
public record BattleRules(
    double DamageShare,
    double WeaknessMultiplier,
    double MinRatio,
    double MaxRatio,
    double RatioScale,
    double PoisonShare,
    double BlindMultiplier,
    int StatusTurns,
    double HandicapStatPenalty
);

/// <param name="Waves">Eight, hardest last. Every one must be won; nothing heals between them.</param>
public record GetArenaRunResponse(
    ChampionEntry Champion,
    int GameId,
    string GameName,
    DateOnly Date,
    BattleRules Rules,
    IReadOnlyList<ArenaWave> Waves
);
