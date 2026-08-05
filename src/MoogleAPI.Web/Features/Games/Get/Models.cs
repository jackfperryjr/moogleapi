namespace MoogleAPI.Web.Features.Games.Get;

public record GetGameRequest(int Id);

public record GetGameResponse(
    int Id,
    string Name,
    int ReleaseYear,
    string Platform,
    string? Description,
    /// <summary>The full logo — the wide lockup with the title text.</summary>
    string? ImageUrl,
    /// <summary>The square emblem — artwork only, no title text.</summary>
    string? ThumbnailUrl,
    int CharacterCount,
    int MonsterCount
);
