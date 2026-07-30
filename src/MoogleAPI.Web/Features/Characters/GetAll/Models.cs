namespace MoogleAPI.Web.Features.Characters.GetAll;

/// <param name="MinPopularity">
/// 0–100 notability floor. Lets a client pull just the well-known characters in one call
/// instead of paging through every walk-on NPC.
/// </param>
/// <param name="RequireImage">
/// Only return characters that have portrait art. Mirrors the same filter on
/// <c>/characters/daily</c> so a puzzle client's guess list matches the answer pool exactly.
/// </param>
public record GetAllCharactersRequest(
    int? GameId,
    int MinPopularity = 0,
    bool RequireImage = false,
    int Page = 1,
    int PageSize = 20);

/// <remarks>
/// Carries the full attribute set so a browser client can bulk-load the character pool once
/// and run guessing, filtering, and comparison locally without further round trips.
/// </remarks>
public record CharacterSummary(
    int Id,
    string Name,
    string? Role,
    string? Affiliation,
    string? Race,
    string? Hometown,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    int Popularity
);

public record GetAllCharactersResponse(IReadOnlyList<CharacterSummary> Items, int TotalCount, int Page, int PageSize);
