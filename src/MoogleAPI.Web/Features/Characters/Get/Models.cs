namespace MoogleAPI.Web.Features.Characters.Get;

public record GetCharacterRequest(int Id);

public record GetCharacterResponse(
    int Id,
    string Name,
    string? Description,
    string? Role,
    string? Affiliation,
    string GameName
);
