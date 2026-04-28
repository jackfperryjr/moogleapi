using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Games.GetAll;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetAllGamesRequest, GetAllGamesResponse>
{
    public override void Configure()
    {
        Get("/games");
        AllowAnonymous();
        Description(b => b
            .WithName("GetAllGames")
            .WithSummary("List all Final Fantasy games")
            .WithTags("Games"));
    }

    public override async Task HandleAsync(GetAllGamesRequest req, CancellationToken ct)
    {
        var cacheKey = $"games:all:page={req.Page}:size={req.PageSize}";

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var total = await db.Games.CountAsync(token);
                var items = await db.Games
                    .OrderBy(g => g.ReleaseYear)
                    .Skip((req.Page - 1) * req.PageSize)
                    .Take(req.PageSize)
                    .Select(g => new GameSummary(g.Id, g.Name, g.ReleaseYear, g.Platform))
                    .ToListAsync(token);

                return new GetAllGamesResponse(items, total, req.Page, req.PageSize);
            },
            cancellationToken: ct
        );

        await Send.OkAsync(response!, ct);
    }
}
