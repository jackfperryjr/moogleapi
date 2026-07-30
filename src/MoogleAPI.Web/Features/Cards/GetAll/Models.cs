namespace MoogleAPI.Web.Features.Cards.GetAll;

/// <param name="Level">Card level 1–10. Triple Triad decks are usually built within a level band.</param>
public record GetAllCardsRequest(int? GameId, int? Level, string? Element, int Page = 1, int PageSize = 50);

/// <remarks>Corner values are 1–10; the games render 10 as "A".</remarks>
public record CardSummary(
    int Id,
    string Name,
    int Top,
    int Left,
    int Right,
    int Bottom,
    string? Element,
    int Level,
    string? CardClass,
    string? ImageUrl,
    string GameName
);

public record GetAllCardsResponse(IReadOnlyList<CardSummary> Items, int TotalCount, int Page, int PageSize);
