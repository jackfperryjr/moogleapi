namespace MoogleAPI.Web.Features.Battle.GetStarters;

/// <param name="GameCount">How many games this monster can be fought as — the run's length.</param>
/// <param name="Games">The games it appears in, oldest first.</param>
public record StarterOption(
    string Family,
    int GameCount,
    string? ImageUrl,
    IReadOnlyList<string> Games
);

public record GetStartersResponse(IReadOnlyList<StarterOption> Starters);
