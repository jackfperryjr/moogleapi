using FastEndpoints;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Features.SphereHunter.Shared;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Puzzles;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.GetDraft;

/// <param name="Date">Which day's hand to deal. Defaults to today; future dates are rejected.</param>
public record GetDraftRequest(DateOnly? Date);

public record GetDraftResponse(
    DateOnly Date, int PartySize, IReadOnlyList<SphereView> Spheres, BattleRules Rules);

/// <summary>
/// The hand a party is drafted from — nine spheres, one per game, the same for everyone that day.
/// </summary>
public class Endpoint(TowerBuilder tower, HybridCache cache)
    : Endpoint<GetDraftRequest, GetDraftResponse>
{
    public override void Configure()
    {
        Get("/sphere-hunter/draft");
        AllowAnonymous();
        Description(b => b
            .WithName("GetSphereDraft")
            .WithSummary("Get the day's nine draftable spheres")
            .WithTags("Sphere Hunter"));
    }

    public override async Task HandleAsync(GetDraftRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DailyPuzzle.Today;

        var response = await cache.GetOrCreateAsync(
            $"spherehunter:draft:{date:yyyy-MM-dd}",
            async token => new GetDraftResponse(
                date,
                TowerBuilder.PartySize,
                [.. (await tower.DraftAsync(date, token)).Select(SphereView.Of)],
                BattleRules.Current),
            tags: CatalogCache.Tags,
            cancellationToken: ct);

        await Send.OkAsync(response, ct);
    }
}
