using FastEndpoints;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.Daily;

public class Endpoint(DailyCharacterSelector selector, HybridCache cache)
    : Endpoint<DailyCharacterRequest, DailyCharacterResponse>
{
    public override void Configure()
    {
        Get("/characters/daily");
        AllowAnonymous();
        Description(b => b
            .WithName("GetDailyCharacter")
            .WithSummary("Get the character puzzle answer for a given day (defaults to today, UTC)")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(DailyCharacterRequest req, CancellationToken ct)
    {
        // Validator rejects future dates, so by here the date is today or earlier.
        var date = req.Date ?? DailyPuzzle.Today;
        var filters = new PuzzleFilters(req.GameId, req.MinPopularity, req.RequireImage);

        var response = await cache.GetOrCreateAsync(
            $"characters:daily:{date:yyyy-MM-dd}:{filters.Scope}",
            async token =>
            {
                var c = await selector.SelectAsync(date, filters, token);
                return c is null
                    ? null
                    : new DailyCharacterResponse(
                        date, c.Id, c.Name, c.Description, c.Role, c.Affiliation, c.Race,
                        c.Hometown, c.ImageUrl, c.Game.Name, c.Game.ReleaseYear, c.Popularity);
            },
            tags: CatalogCache.Tags,
            cancellationToken: ct);

        if (response is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(response, ct);
    }
}
