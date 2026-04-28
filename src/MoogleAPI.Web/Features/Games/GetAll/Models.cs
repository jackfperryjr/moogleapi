namespace MoogleAPI.Web.Features.Games.GetAll;

public record GetAllGamesRequest(int Page = 1, int PageSize = 20);

public record GameSummary(int Id, string Name, int ReleaseYear, string Platform);

public record GetAllGamesResponse(IReadOnlyList<GameSummary> Items, int TotalCount, int Page, int PageSize);
