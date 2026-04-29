using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Characters.Get;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetCharacterRequest, GetCharacterResponse>
{
    public override void Configure()
    {
        Get("/characters/{Id}");
        AllowAnonymous();
        Description(b => b
            .WithName("GetCharacter")
            .WithSummary("Get a Final Fantasy character by ID")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(GetCharacterRequest req, CancellationToken ct)
    {
        var character = await cache.GetOrCreateAsync(
            $"character:{req.Id}",
            async token => await db.Characters
                .Include(c => c.Game)
                .Where(c => c.Id == req.Id)
                .Select(c => new GetCharacterResponse(
                    c.Id, c.Name, c.Description, c.Role, c.Affiliation,
                    c.Race, c.Hometown, c.ImageUrl, c.Game.Name))
                .FirstOrDefaultAsync(token),
            cancellationToken: ct
        );

        if (character is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(character, ct);
    }
}
