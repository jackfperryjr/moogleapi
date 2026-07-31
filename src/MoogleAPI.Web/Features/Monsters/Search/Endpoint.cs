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
            .WithTags("Monsters"));

        Summary(s =>
        {
            s.Summary = "Search monsters by name or description";
            s.Description =
                "Finds monsters whose name or description contains `query`, returning up to 50 matches. " +
                "`query` is required — this endpoint searches *within* a game, it does not list one. " +
                "To list every monster in a game instead, use `GET /api/monsters?gameId=4`.\n\n" +
                "Example: `/api/monsters/search?query=bomb&gameId=4` → Bomb, Bomb King, Gray Bomb, Melt Bomb.";
            s.Params[nameof(SearchMonstersRequest.Query)] =
                "Required, 2–100 characters. Matched as a case-insensitive substring of the monster's name or description.";
            s.Params[nameof(SearchMonstersRequest.GameId)] =
                "Optional. Numeric id from GET /api/games — 1 is Final Fantasy, 4 is Final Fantasy IV, 16 is Final Fantasy XVI.";
            s.Params[nameof(SearchMonstersRequest.Category)] =
                "Optional. Either \"Boss\" or \"Enemy\".";
            s.Responses[200] = "Matches found, or an empty results array if nothing matched the query.";
            s.Responses[400] = "The query parameter was missing, empty, or shorter than 2 characters.";
        });
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
            .Select(m => new MonsterSearchResult(
                m.Id, m.Name, m.Description, m.Category, m.Location, m.HitPoints, m.Level,
                m.Weaknesses, m.Absorbs, m.ImageUrl, m.Game.Name, m.Popularity))
            .ToListAsync(ct);

        await Send.OkAsync(new SearchMonstersResponse(results), ct);
    }
}
