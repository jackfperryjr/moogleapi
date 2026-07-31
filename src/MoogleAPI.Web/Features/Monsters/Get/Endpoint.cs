using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Monsters.Get;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetMonsterRequest, GetMonsterResponse>
{
    public override void Configure()
    {
        Get("/monsters/{Id}");
        AllowAnonymous();
        Description(b => b
            .WithName("GetMonster")
            .WithSummary("Get a Final Fantasy monster by ID")
            .WithTags("Monsters"));
    }

    public override async Task HandleAsync(GetMonsterRequest req, CancellationToken ct)
    {
        var monster = await cache.GetOrCreateAsync(
            $"monster:{req.Id}",
            async token => await db.Monsters
                .Include(m => m.Game)
                .Where(m => m.Id == req.Id)
                .Select(m => new GetMonsterResponse(
                    m.Id, m.Name, m.Description, m.Category, m.Location,
                    m.HitPoints, m.MagicPoints, m.Level, m.Experience, m.Gil,
                    m.Attack, m.Defense, m.MagicAttack, m.MagicDefense, m.Speed, m.Evasion,
                    m.Weaknesses, m.Absorbs, m.Abilities, m.Drops, m.Steals, m.ImageUrl,
                    m.Game.Name, m.Game.ReleaseYear, m.Popularity))
                .FirstOrDefaultAsync(token),
            cancellationToken: ct
        );

        if (monster is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(monster, ct);
    }
}
