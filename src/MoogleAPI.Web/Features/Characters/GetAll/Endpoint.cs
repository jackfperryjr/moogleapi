using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Characters.GetAll;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetAllCharactersRequest, GetAllCharactersResponse>
{
    public override void Configure()
    {
        Get("/characters");
        AllowAnonymous();
        Description(b => b
            .WithName("GetAllCharacters")
            .WithSummary("List all Final Fantasy characters, optionally filtered by game")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(GetAllCharactersRequest req, CancellationToken ct)
    {
        var cacheKey = $"characters:all:game={req.GameId}:page={req.Page}:size={req.PageSize}";

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var query = db.Characters.Include(c => c.Game).AsQueryable();

                if (req.GameId.HasValue)
                    query = query.Where(c => c.GameId == req.GameId.Value);

                var total = await query.CountAsync(token);
                var items = await query
                    .OrderBy(c => c.Name)
                    .Skip((req.Page - 1) * req.PageSize)
                    .Take(req.PageSize)
                    .Select(c => new CharacterSummary(c.Id, c.Name, c.Role, c.Game.Name))
                    .ToListAsync(token);

                return new GetAllCharactersResponse(items, total, req.Page, req.PageSize);
            },
            cancellationToken: ct
        );

        await Send.OkAsync(response!, ct);
    }
}
