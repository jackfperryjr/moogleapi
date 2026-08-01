namespace MoogleAPI.Web.Features.Battle.GetStarters;

/// <summary>
/// The monster as it fights on the first rung — enough to compare two starters before committing
/// to a run, without asking the server to build one.
/// </summary>
/// <param name="GameName">The earliest game the family appears in, where the climb begins.</param>
/// <param name="Weaknesses">Elements this form takes double damage from.</param>
/// <param name="Absorbs">Elements that heal it instead of hurting it.</param>
/// <param name="Moves">Move names it brings to the first rung.</param>
public record StarterFormOption(
    string GameName,
    int HitPoints,
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Speed,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Absorbs,
    IReadOnlyList<string> Moves
);

/// <param name="GameCount">How many games this monster can be fought as — the run's length.</param>
/// <param name="Games">The games it appears in, oldest first.</param>
public record StarterOption(
    string Family,
    int GameCount,
    string? ImageUrl,
    IReadOnlyList<string> Games,
    StarterFormOption StartingForm
);

public record GetStartersResponse(IReadOnlyList<StarterOption> Starters);
