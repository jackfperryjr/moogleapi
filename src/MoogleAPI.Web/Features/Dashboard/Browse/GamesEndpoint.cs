using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Browse;

/// <summary>
/// The games tab, with the row counts each game contributes to the catalogue.
/// </summary>
/// <remarks>
/// Returns every game in one response regardless of paging — there are a few dozen, and the page
/// also uses this list to populate the game filter on the other two tabs, which needs all of them.
/// </remarks>
public class GamesEndpoint(AppDbContext db) : Endpoint<BrowseRequest, BrowseResponse<GameRow>>
{
    public override void Configure()
    {
        Get("/dashboard/games");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(BrowseRequest req, CancellationToken ct)
    {
        var query = db.Games.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(req.Search))
            query = query.Where(g => EF.Functions.ILike(g.Name, $"%{req.Search.Trim()}%"));

        var items = await query
            .OrderBy(g => g.ReleaseYear).ThenBy(g => g.Name)
            .Select(g => new GameRow(
                g.Id, g.Characters.Count, g.Monsters.Count, g.Cards.Count,
                new GameEdit(g.Name, g.ReleaseYear, g.Platform, g.Description,
                             g.ImageUrl, g.ThumbnailUrl, g.ImageSourceUrl)))
            .ToListAsync(ct);

        await Send.OkAsync(new BrowseResponse<GameRow>(items, items.Count, 1, items.Count), ct);
    }
}
