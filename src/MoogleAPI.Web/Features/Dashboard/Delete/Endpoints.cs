using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Delete;

public class DeleteRequest
{
    public int Id { get; set; }
}

/// <param name="Removed">The name of the row that is gone, so the page can say what it deleted.</param>
public record DeleteResponse(int Id, string Removed);

/// <summary>
/// Removes one character.
/// </summary>
/// <remarks>
/// The row goes; its artwork stays in the bucket. Objects are keyed by row id, so orphans cost a
/// few kilobytes each and nothing else, while a generated portrait cost real money to make —
/// roughly four cents and a Gemini call — and deleting a row by mistake should not also destroy
/// the one copy of it. Clearing art is what the image columns in the editor are for.
/// </remarks>
public class CharacterEndpoint(AppDbContext db, HybridCache cache) : Endpoint<DeleteRequest, DeleteResponse>
{
    public override void Configure()
    {
        Delete("/dashboard/characters/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(DeleteRequest req, CancellationToken ct)
    {
        var character = await db.Characters.FirstOrDefaultAsync(c => c.Id == req.Id, ct);
        if (character is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        db.Characters.Remove(character);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new DeleteResponse(character.Id, character.Name), ct);
    }
}

/// <summary>Removes one monster. Its artwork is left in the bucket, as for characters.</summary>
public class MonsterEndpoint(AppDbContext db, HybridCache cache) : Endpoint<DeleteRequest, DeleteResponse>
{
    public override void Configure()
    {
        Delete("/dashboard/monsters/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(DeleteRequest req, CancellationToken ct)
    {
        var monster = await db.Monsters.FirstOrDefaultAsync(m => m.Id == req.Id, ct);
        if (monster is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        db.Monsters.Remove(monster);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new DeleteResponse(monster.Id, monster.Name), ct);
    }
}

/// <summary>
/// Removes one game — but only an empty one.
/// </summary>
/// <remarks>
/// A game is the parent of everything scraped from it, so deleting Final Fantasy VI means
/// deleting several hundred rows behind a single click, and no confirmation dialog conveys that
/// weight honestly. This refuses with the counts instead and leaves the choice of how to clear
/// them — reassign to another game, delete individually — to a deliberate act.
/// </remarks>
public class GameEndpoint(AppDbContext db, HybridCache cache) : Endpoint<DeleteRequest, DeleteResponse>
{
    public override void Configure()
    {
        Delete("/dashboard/games/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(DeleteRequest req, CancellationToken ct)
    {
        var game = await db.Games
            .Include(g => g.Characters)
            .Include(g => g.Monsters)
            .Include(g => g.Cards)
            .FirstOrDefaultAsync(g => g.Id == req.Id, ct);

        if (game is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var held = new List<string>();
        if (game.Characters.Count > 0) held.Add($"{game.Characters.Count} characters");
        if (game.Monsters.Count > 0) held.Add($"{game.Monsters.Count} monsters");
        if (game.Cards.Count > 0) held.Add($"{game.Cards.Count} cards");

        if (held.Count > 0)
        {
            AddError($"{game.Name} still holds {string.Join(", ", held)}. Move or delete them first.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new DeleteResponse(game.Id, game.Name), ct);
    }
}
