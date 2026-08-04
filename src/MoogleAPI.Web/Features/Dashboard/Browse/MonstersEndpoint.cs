using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Browse;

/// <summary>The monsters tab of the dashboard. Uncached for the same reason as the characters tab.</summary>
public class MonstersEndpoint(AppDbContext db) : Endpoint<BrowseRequest, BrowseResponse<MonsterRow>>
{
    public override void Configure()
    {
        Get("/dashboard/monsters");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(BrowseRequest req, CancellationToken ct)
    {
        var query = db.Monsters.AsNoTracking();

        if (req.GameId.HasValue)
            query = query.Where(m => m.GameId == req.GameId.Value);

        // Name *or* description, matching what /api/monsters/search already does. Curation
        // needs the description: what marks a row as not-a-monster — "#REDIRECT ... enemy
        // abilities", "may refer to", "is a genus of" — is only ever in the prose the scrape
        // kept, never in the name. Searching names alone left those rows unreachable here.
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var search = req.Search.Trim();
            query = query.Where(m => EF.Functions.ILike(m.Name, $"%{search}%") ||
                                     (m.Description != null && EF.Functions.ILike(m.Description, $"%{search}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(m => m.Game.ReleaseYear).ThenBy(m => m.Name)
            .Skip(req.Skip)
            .Take(req.SafePageSize)
            .Select(m => new MonsterRow(
                m.Id, m.Game.Name, m.Game.ReleaseYear,
                new MonsterEdit(
                    m.Name, m.Description, m.Category, m.Location, m.HitPoints, m.MagicPoints,
                    m.Level, m.Experience, m.Gil, m.Attack, m.Defense, m.MagicAttack,
                    m.MagicDefense, m.Speed, m.Evasion, m.Abilities, m.Drops, m.Steals,
                    m.Weaknesses, m.Absorbs, m.Popularity, m.WikiPageLength, m.WikiBacklinks,
                    m.ImageUrl, m.ImageSourceUrl, m.GeneratedImageUrl, m.ImageKind, m.GameId)))
            .ToListAsync(ct);

        await Send.OkAsync(new BrowseResponse<MonsterRow>(items, total, req.SafePage, req.SafePageSize), ct);
    }
}
