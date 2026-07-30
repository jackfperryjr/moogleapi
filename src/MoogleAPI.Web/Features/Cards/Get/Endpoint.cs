using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Cards.Get;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetCardRequest, GetCardResponse>
{
    public override void Configure()
    {
        Get("/cards/{Id}");
        AllowAnonymous();
        Description(b => b
            .WithName("GetCard")
            .WithSummary("Get a Triple Triad card by ID")
            .WithTags("Cards"));
    }

    public override async Task HandleAsync(GetCardRequest req, CancellationToken ct)
    {
        var card = await cache.GetOrCreateAsync(
            $"card:{req.Id}",
            async token => await db.Cards
                .Include(c => c.Game)
                .Where(c => c.Id == req.Id)
                .Select(c => new GetCardResponse(
                    c.Id, c.Name, c.Top, c.Left, c.Right, c.Bottom,
                    c.Element, c.Level, c.CardClass, c.ImageUrl, c.Game.Name))
                .FirstOrDefaultAsync(token),
            cancellationToken: ct
        );

        if (card is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(card, ct);
    }
}
