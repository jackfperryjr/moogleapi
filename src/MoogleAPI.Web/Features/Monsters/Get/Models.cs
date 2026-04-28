namespace MoogleAPI.Web.Features.Monsters.Get;

public record GetMonsterRequest(int Id);

public record GetMonsterResponse(
    int Id,
    string Name,
    string? Description,
    string? Category,
    int? HitPoints,
    string GameName
);
