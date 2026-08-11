using FastEndpoints;
using MoogleAPI.Web.Features.SphereHunter.Shared;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.GetDraft;

/// <param name="Run">
/// An opaque token identifying this run, made up by the client. Everything about the run is derived
/// from it, so the same token always deals the same nine spheres — which is what lets a player
/// refresh mid-climb — and a new token is a new run.
/// </param>
public record GetDraftRequest(string Run);

public record GetDraftResponse(
    string Run, int PartySize, IReadOnlyList<SphereView> Spheres, BattleRules Rules);

/// <summary>
/// The nine spheres a party is drafted from.
/// </summary>
/// <remarks>
/// Not cached. The response is unique per run token, so a cache here would be a dictionary that
/// only ever grows and never gets a second read; the expensive part — the sphere pool itself — is
/// already cached behind <see cref="SpherePool"/>.
/// </remarks>
public class Endpoint(HuntBuilder expedition) : Endpoint<GetDraftRequest, GetDraftResponse>
{
    public override void Configure()
    {
        Get("/sphere-hunter/draft");
        AllowAnonymous();
        Description(b => b
            .WithName("GetSphereDraft")
            .WithSummary("Get nine draftable spheres for a run")
            .WithTags("Sphere Hunter"));
    }

    public override async Task HandleAsync(GetDraftRequest req, CancellationToken ct)
    {
        await Send.OkAsync(new GetDraftResponse(
            req.Run,
            HuntBuilder.PartySize,
            [.. (await expedition.DraftAsync(req.Run, ct)).Select(SphereView.Of)],
            BattleRules.Current), ct);
    }
}
