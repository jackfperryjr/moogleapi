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
            .WithSummary("List all Final Fantasy monsters, optionally filtered by game, category, notability, or artwork")
            .WithTags("Monsters"));
    }

    public override async Task HandleAsync(GetAllMonstersRequest req, CancellationToken ct)
    {
        var cacheKey = $"monsters:all:game={req.GameId}:cat={req.Category}:pop={req.MinPopularity}:img={req.RequireImage}:page={req.Page}:size={req.PageSize}";

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var query = db.Monsters.Include(m => m.Game).AsQueryable();

                if (req.GameId.HasValue)
                    query = query.Where(m => m.GameId == req.GameId.Value);

                if (!string.IsNullOrWhiteSpace(req.Category))
                    query = query.Where(m => m.Category == req.Category);

                if (req.MinPopularity > 0)
                    query = query.Where(m => m.Popularity >= req.MinPopularity);

                if (req.RequireImage)
                    query = query.Where(m => m.ImageUrl != null);

                var total = await query.CountAsync(token);
                var items = await query
                    .OrderBy(m => m.Name)
                    .Skip((req.Page - 1) * req.PageSize)
                    .Take(req.PageSize)
                    .Select(m => new MonsterSummary(
                        m.Id, m.Name, m.Category, m.Location, m.HitPoints, m.Level,
                        m.Attack, m.Defense, m.MagicAttack, m.MagicDefense, m.Speed,
                        m.Weaknesses, m.Absorbs, m.Abilities,
                        m.ImageUrl, m.Game.Name, m.Game.ReleaseYear, m.Popularity))
                    .ToListAsync(token);

                return new GetAllMonstersResponse(items, total, req.Page, req.PageSize);
            },
            tags: CatalogCache.Tags,
            cancellationToken: ct
        );

        await Send.OkAsync(response!, ct);
    }
}
