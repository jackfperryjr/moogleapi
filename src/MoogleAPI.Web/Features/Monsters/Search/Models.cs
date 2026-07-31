namespace MoogleAPI.Web.Features.Monsters.Search;

/// <param name="Category">"Boss" or "Enemy".</param>
public record SearchMonstersRequest(string Query, int? GameId, string? Category);

public record MonsterSearchResult(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string? Location,
    int? HitPoints,
    int? Level,
    string? Weaknesses,
    string? Absorbs,
    string? ImageUrl,
    string GameName,
    int Popularity
);

public record SearchMonstersResponse(IReadOnlyList<MonsterSearchResult> Results);
