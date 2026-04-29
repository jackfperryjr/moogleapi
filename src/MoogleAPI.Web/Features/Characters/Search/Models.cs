namespace MoogleAPI.Web.Features.Characters.Search;

public record SearchCharactersRequest(string Query, int? GameId);

public record SearchResult(int Id, string Name, string? Role, string? Description, string? ImageUrl, string GameName);

public record SearchCharactersResponse(IReadOnlyList<SearchResult> Results);
