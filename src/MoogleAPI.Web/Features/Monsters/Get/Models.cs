namespace MoogleAPI.Web.Features.Monsters.Get;

public record GetMonsterRequest(int Id);

/// <param name="Weaknesses">Comma-separated elements the monster takes extra damage from.</param>
/// <param name="Absorbs">Comma-separated elements the monster heals from.</param>
/// <param name="Abilities">Comma-separated moves the monster uses in battle.</param>
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
    int? Attack,
    int? Defense,
    int? MagicAttack,
    int? MagicDefense,
    int? Speed,
    int? Evasion,
    string? Weaknesses,
    string? Absorbs,
    string? Abilities,
    string? Drops,
    string? Steals,
    string? ImageUrl,
    string GameName,
    int ReleaseYear,
    int Popularity
);
