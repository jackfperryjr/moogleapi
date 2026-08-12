namespace MoogleAPI.Web.Features.Games.Get;

public record GetGameRequest(int Id);

public record GetGameResponse(
    int Id,
    string Name,
    int ReleaseYear,
    string Platform,
    /// <summary>
    /// True for a numbered entry or one of their direct sequels; false for a spin-off.
    /// </summary>
    bool IsMainSeries,
    string? Description,
    /// <summary>The full logo — the wide lockup with the title text.</summary>
    string? ImageUrl,
    /// <summary>The square emblem — artwork only, no title text.</summary>
    string? ThumbnailUrl,
    int CharacterCount,
    int MonsterCount
);
