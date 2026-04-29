using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Characters.Search;

public class Endpoint(AppDbContext db) : Endpoint<SearchCharactersRequest, SearchCharactersResponse>
{
    public override void Configure()
    {
        Get("/characters/search");
        AllowAnonymous();
        Description(b => b
            .WithName("SearchCharacters")
            .WithSummary("Search characters by name or description")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(SearchCharactersRequest req, CancellationToken ct)
    {
        var term = req.Query.ToLower();
        var query = db.Characters.Include(c => c.Game).AsQueryable();

        if (req.GameId.HasValue)
            query = query.Where(c => c.GameId == req.GameId.Value);

        var results = await query
            .Where(c => EF.Functions.ILike(c.Name, $"%{term}%") ||
                        (c.Description != null && EF.Functions.ILike(c.Description, $"%{term}%")))
            .OrderBy(c => c.Name)
            .Take(50)
            .Select(c => new SearchResult(c.Id, c.Name, c.Role, c.Description, c.ImageUrl, c.Game.Name))
            .ToListAsync(ct);

        await Send.OkAsync(new SearchCharactersResponse(results), ct);
    }
}
