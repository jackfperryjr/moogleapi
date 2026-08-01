namespace MoogleAPI.Web.Features.Battle.GetRun;

/// <param name="Family">Monster name to run with, from GET /api/battle/starters.</param>
/// <param name="Date">Which day's run. Defaults to today; future dates are rejected.</param>
public record GetRunRequest(string Family, DateOnly? Date);

/// <param name="Element">Fire, Ice, Thunder, Water, Earth, Wind, Holy, Dark — null is non-elemental.</param>
/// <param name="Kind">Physical is reduced by Defense, Magic by MagicDefense.</param>
/// <param name="Power">Multiplier on the attacker's offence stat.</param>
/// <param name="Recoil">Fraction of the user's own max HP the move costs, 0 for most moves.</param>
/// <param name="Status">
/// Condition inflicted on the defender: "Poison", "Blind", "Silence", or "None". A move that
/// inflicts one hits softer in exchange.
/// </param>
public record MoveOption(string Name, string? Element, string Kind, double Power, double Recoil, string Status);

/// <param name="Weaknesses">Elements this monster takes double damage from.</param>
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

/// <param name="Number">1-based position in the ladder.</param>
/// <param name="Opponents">Two rank-and-file enemies, then the rung's boss.</param>
public record RunRung(
    int Number,
    int GameId,
    string GameName,
    Combatant Player,
    IReadOnlyList<Combatant> Opponents
);

public record SkippedRung(int GameId, string GameName, string Reason);

/// <summary>
/// The damage model the client resolves battles with. Sent rather than hardcoded in the
/// browser because the server vets every matchup with this same arithmetic — two copies would
/// drift, and the promise that a fight is winnable would stop being true.
/// </summary>
/// <param name="DamageShare">Damage as a fraction of the defender's maximum HP.</param>
/// <param name="WeaknessMultiplier">Applied when a move's element is one of the defender's weaknesses.</param>
/// <param name="MinRatio">Floor on the attack-to-defence ratio, so no monster is unhittable.</param>
/// <param name="MaxRatio">Ceiling on it, so no monster is one-shot.</param>
/// <param name="PoisonShare">Share of maximum HP Poison bleeds after the afflicted acts.</param>
/// <param name="BlindMultiplier">What a Physical move is worth while its user is Blind.</param>
/// <param name="StatusTurns">How long a status lasts, in the afflicted combatant's own turns.</param>
public record BattleRules(
    double DamageShare,
    double WeaknessMultiplier,
    double MinRatio,
    double MaxRatio,
    double PoisonShare,
    double BlindMultiplier,
    int StatusTurns
);

/// <param name="RetriesPerRun">
/// Second chances for the whole run, not per rung. Every battle on a rung must be won to
/// advance — including the boss — so a loss ends the run unless a retry is spent replaying
/// that battle.
/// </param>
public record GetRunResponse(
    string Family,
    DateOnly Date,
    int RetriesPerRun,
    BattleRules Rules,
    IReadOnlyList<RunRung> Rungs,
    IReadOnlyList<SkippedRung> Skipped
);
