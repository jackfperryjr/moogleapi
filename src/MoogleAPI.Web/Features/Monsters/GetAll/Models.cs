namespace MoogleAPI.Web.Features.Monsters.GetAll;

/// <param name="Category">"Boss" or "Enemy".</param>
/// <param name="MinPopularity">
/// 0–100 notability floor. Lets a client pull just the recognizable monsters in one call
/// instead of paging through every random-encounter fiend.
/// </param>
/// <param name="RequireImage">Only return monsters that have artwork.</param>
public record GetAllMonstersRequest(
    int? GameId,
    string? Category,
    int MinPopularity = 0,
    bool RequireImage = false,
    int Page = 1,
    int PageSize = 20);

/// <remarks>
/// Carries the full attribute set for the same reason <c>CharacterSummary</c> does: a browser
/// client bulk-loads the pool once and then compares and filters locally.
/// </remarks>
public record MonsterSummary(
    int Id,
    string Name,
    string? Category,
    string? Location,
    int? HitPoints,
    int? Level,
    int? Attack,
    int? Defense,
    int? MagicAttack,
    int? MagicDefense,
    int? Speed,
    string? Weaknesses,
    string? Absorbs,
    string? Abilities,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    int Popularity
);

public record GetAllMonstersResponse(IReadOnlyList<MonsterSummary> Items, int TotalCount, int Page, int PageSize);
