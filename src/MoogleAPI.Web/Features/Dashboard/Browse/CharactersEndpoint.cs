using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Browse;

/// <summary>
/// The characters tab of the dashboard. Deliberately uncached, unlike <c>/api/characters</c>:
/// the point of this page is to see the table as it is right now, straight after a scrape or an
/// image pass, not what the ten-minute cache still believes.
/// </summary>
public class CharactersEndpoint(AppDbContext db) : Endpoint<BrowseRequest, BrowseResponse<CharacterRow>>
{
    public override void Configure()
    {
        Get("/dashboard/characters");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        // The owner's own curation traffic should not compete with the public 60/min partition
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(BrowseRequest req, CancellationToken ct)
    {
        var query = db.Characters.AsNoTracking();

        if (req.GameId.HasValue)
            query = query.Where(c => c.GameId == req.GameId.Value);

        if (!string.IsNullOrWhiteSpace(req.Search))
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{req.Search.Trim()}%"));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Game.ReleaseYear).ThenBy(c => c.Name)
            .Skip(req.Skip)
            .Take(req.SafePageSize)
            .Select(c => new CharacterRow(
                c.Id, c.Game.Name, c.Game.ReleaseYear,
                new CharacterEdit(
                    c.Name, c.Description, c.Role, c.Affiliation, c.Race, c.Hometown, c.Job,
                    c.Weapon, c.Abilities, c.IsPlayable, c.Popularity, c.WikiPageLength,
                    c.WikiBacklinks, c.ImageUrl, c.ImageSourceUrl, c.GeneratedImageUrl,
                    c.ImageKind, c.GameId)))
            .ToListAsync(ct);

        await Send.OkAsync(new BrowseResponse<CharacterRow>(items, total, req.SafePage, req.SafePageSize), ct);
    }
}
