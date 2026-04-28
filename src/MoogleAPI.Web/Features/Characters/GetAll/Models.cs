namespace MoogleAPI.Web.Features.Characters.GetAll;

public record GetAllCharactersRequest(int? GameId, int Page = 1, int PageSize = 20);

public record CharacterSummary(int Id, string Name, string? Role, string GameName);

public record GetAllCharactersResponse(IReadOnlyList<CharacterSummary> Items, int TotalCount, int Page, int PageSize);
