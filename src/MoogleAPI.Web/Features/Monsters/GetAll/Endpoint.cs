using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Monsters.GetAll;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetAllMonstersRequest, GetAllMonstersResponse>
{
    public override void Configure()
    {
        Get("/monsters");
        AllowAnonymous();
        Description(b => b
            .WithName("GetAllMonsters")
            .WithSummary("List all Final Fantasy monsters, optionally filtered by game or category")
            .WithTags("Monsters"));
    }

    public override async Task HandleAsync(GetAllMonstersRequest req, CancellationToken ct)
    {
        var cacheKey = $"monsters:all:game={req.GameId}:cat={req.Category}:page={req.Page}:size={req.PageSize}";

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var query = db.Monsters.Include(m => m.Game).AsQueryable();

                if (req.GameId.HasValue)
                    query = query.Where(m => m.GameId == req.GameId.Value);

                if (!string.IsNullOrWhiteSpace(req.Category))
                    query = query.Where(m => m.Category == req.Category);

                var total = await query.CountAsync(token);
                var items = await query
                    .OrderBy(m => m.Name)
                    .Skip((req.Page - 1) * req.PageSize)
                    .Take(req.PageSize)
                    .Select(m => new MonsterSummary(m.Id, m.Name, m.Category, m.HitPoints, m.Game.Name))
                    .ToListAsync(token);

                return new GetAllMonstersResponse(items, total, req.Page, req.PageSize);
            },
            cancellationToken: ct
        );

        await Send.OkAsync(response!, ct);
    }
}
