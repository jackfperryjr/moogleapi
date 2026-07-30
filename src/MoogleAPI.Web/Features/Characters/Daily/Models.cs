namespace MoogleAPI.Web.Features.Characters.Daily;

/// <param name="Date">
/// Which day's puzzle to fetch, UTC. Defaults to today. Past dates are allowed so players can
/// catch up on puzzles they missed; future dates are rejected.
/// </param>
/// <param name="MinPopularity">
/// 0–100 notability floor — the difficulty dial. Each value is its own puzzle series with its
/// own answer for the day.
/// </param>
/// <param name="RequireImage">Only pick characters that have portrait art.</param>
public record DailyCharacterRequest(
    int? GameId,
    DateOnly? Date,
    int MinPopularity = 0,
    bool RequireImage = false
);

public record DailyCharacterResponse(
    DateOnly Date,
    int Id,
    string Name,
    string? Description,
    string? Role,
    string? Affiliation,
    string? Race,
    string? Hometown,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    int Popularity
);
