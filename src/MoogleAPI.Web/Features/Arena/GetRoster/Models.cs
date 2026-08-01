namespace MoogleAPI.Web.Features.Arena.GetRoster;

/// <param name="GameId">Optional. Restrict the roster to one game.</param>
public record GetRosterRequest(int? GameId);

/// <param name="Archetype">
/// How the character fights — Warrior, Mage, Scout or Balanced. Derived from their job, weapon
/// and abilities, and what decides their stat weighting.
/// </param>
/// <param name="RecommendedLevel">
/// The level this character clears today's eight waves at with health to spare. A starting
/// point, not a cap: any level from 1 to 99 can be requested.
/// </param>
public record RosterCharacter(
    int CharacterId,
    string Name,
    int GameId,
    string GameName,
    string Archetype,
    string? Job,
    string? Weapon,
    string? ImageUrl,
    int Popularity,
    int RecommendedLevel
);

/// <param name="Games">The games with a roster, in release order — for a picker.</param>
public record GetRosterResponse(
    IReadOnlyList<RosterCharacter> Characters,
    IReadOnlyList<RosterGame> Games
);

public record RosterGame(int GameId, string GameName, int CharacterCount);
