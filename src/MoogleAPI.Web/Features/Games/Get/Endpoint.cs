using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Games.Get;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetGameRequest, GetGameResponse>
{
    public override void Configure()
    {
        Get("/games/{Id}");
        AllowAnonymous();
        Description(b => b
            .WithName("GetGame")
            .WithSummary("Get a Final Fantasy game by ID")
            .WithTags("Games"));
    }

    public override async Task HandleAsync(GetGameRequest req, CancellationToken ct)
    {
        var game = await cache.GetOrCreateAsync(
            $"game:{req.Id}",
            async token => await db.Games
                .Where(g => g.Id == req.Id)
                .Select(g => new GetGameResponse(
                    g.Id, g.Name, g.ReleaseYear, g.Platform, g.Description,
                    g.Characters.Count, g.Monsters.Count))
                .FirstOrDefaultAsync(token),
            cancellationToken: ct
        );

        if (game is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(game, ct);
    }
}
