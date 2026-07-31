using FastEndpoints;
using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Features.Battle.GetStarters;

public class Endpoint(GauntletBuilder gauntlet) : EndpointWithoutRequest<GetStartersResponse>
{
    public override void Configure()
    {
        Get("/battle/starters");
        AllowAnonymous();
        Description(b => b
            .WithName("GetBattleStarters")
            .WithTags("Battle"));

        Summary(s =>
        {
            s.Summary = "List the monsters that can be taken through a gauntlet run";
            s.Description =
                "A monster can start a run when the same name appears, with stats and artwork, in at least " +
                "five of the ladder's games — that recurrence is what the run's evolution steps follow. " +
                "Bomb reaches the most games, then Behemoth, Goblin and Adamantoise.\n\n" +
                "Pass the chosen `family` to `GET /api/battle/run`.";
            s.Responses[200] = "Starters, longest run first.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var starters = await gauntlet.GetStartersAsync(ct);

        await Send.OkAsync(
            new GetStartersResponse(
                starters.Select(s => new StarterOption(s.Family, s.GameCount, s.ImageUrl, s.Games)).ToList()),
            ct);
    }
}
