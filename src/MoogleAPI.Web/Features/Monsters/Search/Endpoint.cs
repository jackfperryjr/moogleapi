using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Monsters.Search;

public class Endpoint(AppDbContext db) : Endpoint<SearchMonstersRequest, SearchMonstersResponse>
{
    public override void Configure()
    {
        Get("/monsters/search");
        AllowAnonymous();
        Description(b => b
            .WithName("SearchMonsters")
            .WithSummary("Search monsters by name or description")
            .WithTags("Monsters"));
    }

    public override async Task HandleAsync(SearchMonstersRequest req, CancellationToken ct)
    {
        var query = db.Monsters.Include(m => m.Game).AsQueryable();

        if (req.GameId.HasValue)
            query = query.Where(m => m.GameId == req.GameId.Value);

        if (!string.IsNullOrWhiteSpace(req.Category))
            query = query.Where(m => m.Category == req.Category);

        var results = await query
            .Where(m => EF.Functions.ILike(m.Name, $"%{req.Query}%") ||
                        (m.Description != null && EF.Functions.ILike(m.Description, $"%{req.Query}%")))
            .OrderBy(m => m.Name)
            .Take(50)
            .Select(m => new MonsterSearchResult(m.Id, m.Name, m.Category, m.HitPoints, m.Description, m.Game.Name))
            .ToListAsync(ct);

        await Send.OkAsync(new SearchMonstersResponse(results), ct);
    }
}
