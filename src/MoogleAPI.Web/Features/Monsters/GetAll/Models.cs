namespace MoogleAPI.Web.Features.Monsters.GetAll;

public record GetAllMonstersRequest(int? GameId, string? Category, int Page = 1, int PageSize = 20);

public record MonsterSummary(int Id, string Name, string? Category, int? HitPoints, string GameName);

public record GetAllMonstersResponse(IReadOnlyList<MonsterSummary> Items, int TotalCount, int Page, int PageSize);
