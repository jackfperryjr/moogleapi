namespace MoogleAPI.Web.Features.Cards.Get;

public record GetCardRequest(int Id);

public record GetCardResponse(
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
