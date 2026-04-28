namespace MoogleAPI.Web.Features.Games.Get;

public record GetGameRequest(int Id);

public record GetGameResponse(
    int Id,
    string Name,
    int ReleaseYear,
    string Platform,
    string? Description,
    int CharacterCount,
    int MonsterCount
);
