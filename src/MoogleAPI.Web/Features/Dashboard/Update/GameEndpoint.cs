using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Update;

/// <summary>
/// Writes one hand-edited game back. Renaming one rewrites the label on every character and
/// monster it owns, so the cache purge that follows matters more here than anywhere else.
/// </summary>
public class GameEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<UpdateGameRequest, UpdateResponse<GameRow>>
{
    public override void Configure()
    {
        Put("/dashboard/games/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(UpdateGameRequest req, CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == req.Id, ct);
        if (game is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var f = req.Fields;
        game.Name = f.Name.Trim();
        game.ReleaseYear = f.ReleaseYear;
        game.Platform = f.Platform.Trim();
        game.IsMainSeries = f.IsMainSeries;
        game.Description = EditText.Clean(f.Description);
        game.ImageUrl = EditText.Clean(f.ImageUrl);
        game.ThumbnailUrl = EditText.Clean(f.ThumbnailUrl);
        game.ImageSourceUrl = EditText.Clean(f.ImageSourceUrl);

        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        var counts = await db.Games
            .Where(g => g.Id == game.Id)
            .Select(g => new { Characters = g.Characters.Count, Monsters = g.Monsters.Count, Cards = g.Cards.Count })
            .SingleAsync(ct);

        await Send.OkAsync(new UpdateResponse<GameRow>(
            new GameRow(game.Id, counts.Characters, counts.Monsters, counts.Cards,
                new GameEdit(game.Name, game.ReleaseYear, game.Platform, game.IsMainSeries, game.Description,
                             game.ImageUrl, game.ThumbnailUrl, game.ImageSourceUrl))), ct);
    }
}
