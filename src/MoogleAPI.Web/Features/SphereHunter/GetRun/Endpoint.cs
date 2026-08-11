using FastEndpoints;
using MoogleAPI.Web.Features.SphereHunter.Shared;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.GetRun;

/// <param name="Spheres">
/// The party, as a comma-separated list of sphere ids — up to three, from the day's draft. Sent
/// rather than stored: the site has no player accounts, so the party lives in the browser and is
/// handed back on each request.
/// </param>
/// <param name="Run">
/// The run token, as handed to <c>/sphere-hunter/draft</c>. The same token and the same party
/// always rebuild the same tower, which is what lets a player refresh mid-climb.
/// </param>
public record GetRunRequest(string Spheres, string Run);

/// <param name="Capture">The fiend offered for sealing once the floor is cleared.</param>
public record FloorView(
    int Number, int GameId, string GameName, int Level,
    IReadOnlyList<SphereView> Opponents, SphereView Capture);

public record SkippedView(int GameId, string GameName, string Reason);

public record GetRunResponse(
    string Run,
    IReadOnlyList<SphereView> Party,
    IReadOnlyList<FloorView> Floors,
    IReadOnlyList<SkippedView> Skipped,
    BattleRules Rules);

/// <summary>
/// A whole tower in one response: the party, every floor, every opponent, and the rules the
/// browser resolves them with. No server-side battle state — a run costs one request.
/// </summary>
public class Endpoint(TowerBuilder tower) : Endpoint<GetRunRequest, GetRunResponse>
{
    public override void Configure()
    {
        Get("/sphere-hunter/run");
        AllowAnonymous();
        Description(b => b
            .WithName("GetSphereHunterRun")
            .WithSummary("Build a tower run for a party of spheres")
            .WithTags("Sphere Hunter"));
    }

    public override async Task HandleAsync(GetRunRequest req, CancellationToken ct)
    {
        var ids = ParseIds(req.Spheres);

        var run = await tower.BuildAsync(ids, req.Run, ct);
        if (run is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new GetRunResponse(
            run.Run,
            [.. run.Party.Select(SphereView.Of)],
            [.. run.Floors.Select(f => new FloorView(
                f.Number, f.GameId, f.GameName, f.Level,
                [.. f.Opponents.Select(SphereView.Of)],
                SphereView.Of(f.Capture)))],
            [.. run.Skipped.Select(s => new SkippedView(s.GameId, s.GameName, s.Reason))],
            BattleRules.Current), ct);
    }

    /// <summary>Anything unparseable is dropped, and the validator has already refused an empty list.</summary>
    internal static List<int> ParseIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(part => int.TryParse(part, out var id) ? id : 0)
                       .Where(id => id > 0)
                       .Distinct()];
}
