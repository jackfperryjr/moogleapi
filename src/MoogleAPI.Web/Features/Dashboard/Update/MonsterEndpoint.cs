using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Update;

/// <summary>Writes one hand-edited monster back. Same rules as the character endpoint.</summary>
public class MonsterEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<UpdateMonsterRequest, UpdateResponse<MonsterRow>>
{
    public override void Configure()
    {
        Put("/dashboard/monsters/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(UpdateMonsterRequest req, CancellationToken ct)
    {
        var monster = await db.Monsters.FirstOrDefaultAsync(m => m.Id == req.Id, ct);
        if (monster is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == req.Fields.GameId, ct);
        if (game is null)
        {
            AddError(r => r.Fields.GameId, $"No game with id {req.Fields.GameId}.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var f = req.Fields;
        var name = f.Name.Trim();

        // Renaming into a name the same game already uses would hit the unique index on
        // (Name, GameId) and come back as a 500. Curation renames rows constantly, so the
        // collision is reported as a conflict against the row that holds the name.
        var clash = await db.Monsters
            .FirstOrDefaultAsync(x => x.Id != monster.Id && x.GameId == game.Id &&
                                      x.Name.ToLower() == name.ToLower(), ct);

        if (clash is not null)
        {
            AddError(r => r.Fields.Name,
                $"{game.Name} already has a monster called {clash.Name} (#{clash.Id}).");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        monster.Name = name;
        monster.Description = EditText.Clean(f.Description);
        monster.Category = EditText.Clean(f.Category);
        monster.Location = EditText.Clean(f.Location);
        monster.HitPoints = f.HitPoints;
        monster.MagicPoints = f.MagicPoints;
        monster.Level = f.Level;
        monster.Experience = f.Experience;
        monster.Gil = f.Gil;
        monster.Attack = f.Attack;
        monster.Defense = f.Defense;
        monster.MagicAttack = f.MagicAttack;
        monster.MagicDefense = f.MagicDefense;
        monster.Speed = f.Speed;
        monster.Evasion = f.Evasion;
        monster.Abilities = EditText.Clean(f.Abilities);
        monster.Drops = EditText.Clean(f.Drops);
        monster.Steals = EditText.Clean(f.Steals);
        monster.Weaknesses = EditText.Clean(f.Weaknesses);
        monster.Absorbs = EditText.Clean(f.Absorbs);
        monster.Popularity = f.Popularity;
        monster.WikiPageLength = f.WikiPageLength;
        monster.WikiBacklinks = f.WikiBacklinks;
        monster.ImageUrl = EditText.Clean(f.ImageUrl);
        monster.ImageSourceUrl = EditText.Clean(f.ImageSourceUrl);
        monster.GeneratedImageUrl = EditText.Clean(f.GeneratedImageUrl);
        monster.ImageKind = EditText.Clean(f.ImageKind);
        monster.GameId = game.Id;

        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new UpdateResponse<MonsterRow>(
            new MonsterRow(monster.Id, game.Name, game.ReleaseYear, new MonsterEdit(
                monster.Name, monster.Description, monster.Category, monster.Location,
                monster.HitPoints, monster.MagicPoints, monster.Level, monster.Experience,
                monster.Gil, monster.Attack, monster.Defense, monster.MagicAttack,
                monster.MagicDefense, monster.Speed, monster.Evasion, monster.Abilities,
                monster.Drops, monster.Steals, monster.Weaknesses, monster.Absorbs,
                monster.Popularity, monster.WikiPageLength, monster.WikiBacklinks,
                monster.ImageUrl, monster.ImageSourceUrl, monster.GeneratedImageUrl,
                monster.ImageKind, monster.GameId))), ct);
    }
}
