namespace MoogleAPI.Web.Features.Characters.Random;

/// <param name="Seed">
/// Makes the pick deterministic — useful for shareable challenge links, where the seed is
/// meant to be public. Do NOT use a date here to build a daily puzzle: the caller controls
/// the seed, so players could read off every future answer. Use <c>/characters/daily</c>,
/// which derives the seed server-side from a secret.
/// </param>
/// <param name="MinPopularity">
/// 0–100 notability floor. Filters out obscure walk-on NPCs — the difficulty dial.
/// </param>
/// <param name="RequireImage">Only return characters that have portrait art.</param>
public record RandomCharacterRequest(
    int? GameId,
    string? Seed,
    int MinPopularity = 0,
    bool RequireImage = false
);

public record RandomCharacterResponse(
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
