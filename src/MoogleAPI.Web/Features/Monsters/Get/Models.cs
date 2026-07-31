namespace MoogleAPI.Web.Features.Monsters.Get;

public record GetMonsterRequest(int Id);

/// <param name="Weaknesses">Comma-separated elements the monster takes extra damage from.</param>
/// <param name="Absorbs">Comma-separated elements the monster heals from.</param>
public record GetMonsterResponse(
    int Id,
    string Name,
    string? Description,
    string? Category,
    string? Location,
    int? HitPoints,
    int? MagicPoints,
    int? Level,
    int? Experience,
    int? Gil,
    string? Weaknesses,
    string? Absorbs,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    int Popularity
);
