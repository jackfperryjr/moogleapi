using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Cards.GetAll;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<GetAllCardsRequest, GetAllCardsResponse>
{
    public override void Configure()
    {
        Get("/cards");
        AllowAnonymous();
        Description(b => b
            .WithName("GetAllCards")
            .WithSummary("List Triple Triad cards, optionally filtered by game, level, or element")
            .WithTags("Cards"));
    }

    public override async Task HandleAsync(GetAllCardsRequest req, CancellationToken ct)
    {
        var cacheKey = $"cards:all:game={req.GameId}:level={req.Level}:element={req.Element}:page={req.Page}:size={req.PageSize}";

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var query = db.Cards.Include(c => c.Game).AsQueryable();

                if (req.GameId.HasValue)
                    query = query.Where(c => c.GameId == req.GameId.Value);

                if (req.Level.HasValue)
                    query = query.Where(c => c.Level == req.Level.Value);

                if (!string.IsNullOrWhiteSpace(req.Element))
                    query = query.Where(c => c.Element != null && c.Element.ToLower() == req.Element.ToLower());

                var total = await query.CountAsync(token);
                var items = await query
                    .OrderBy(c => c.Level).ThenBy(c => c.Name)
                    .Skip((req.Page - 1) * req.PageSize)
                    .Take(req.PageSize)
                    .Select(c => new CardSummary(
                        c.Id, c.Name, c.Top, c.Left, c.Right, c.Bottom,
                        c.Element, c.Level, c.CardClass, c.ImageUrl, c.Game.Name))
                    .ToListAsync(token);

                return new GetAllCardsResponse(items, total, req.Page, req.PageSize);
            },
            tags: CatalogCache.Tags,
            cancellationToken: ct
        );

        await Send.OkAsync(response!, ct);
    }
}
