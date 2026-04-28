namespace MoogleAPI.Web.Features.Monsters.Search;

public record SearchMonstersRequest(string Query, int? GameId, string? Category);

public record MonsterSearchResult(int Id, string Name, string? Category, int? HitPoints, string? Description, string GameName);

public record SearchMonstersResponse(IReadOnlyList<MonsterSearchResult> Results);
