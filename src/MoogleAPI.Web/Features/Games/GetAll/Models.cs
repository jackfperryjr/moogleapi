namespace MoogleAPI.Web.Features.Games.GetAll;

public record GetAllGamesRequest(int Page = 1, int PageSize = 20);

public record GameSummary(
    int Id, string Name, int ReleaseYear, string Platform,
    /// <summary>
    /// True for a numbered entry or one of their direct sequels; false for a spin-off.
    /// </summary>
    bool IsMainSeries,
    /// <summary>The full logo — the wide lockup with the title text.</summary>
    string? ImageUrl,
    /// <summary>The square emblem — artwork only, no title text.</summary>
    string? ThumbnailUrl);

public record GetAllGamesResponse(IReadOnlyList<GameSummary> Items, int TotalCount, int Page, int PageSize);
