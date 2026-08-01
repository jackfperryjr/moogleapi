using FastEndpoints;
using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Battle.GetRun;

/// <remarks>
/// The whole run ships in one response — every rung, both sides, and their moves. Combat then
/// resolves in the browser with no further calls, which is what keeps a twelve-rung run inside
/// the anonymous rate limit and lets a player finish one on a flaky connection.
/// </remarks>
public class Endpoint(ClimbBuilder climb) : Endpoint<GetRunRequest, GetRunResponse>
{
    /// <summary>
    /// Every battle on a rung has to be won, so the boss at the end of each one is a real wall
    /// rather than a fight you can shrug off. The give is here instead: three retries spanning
    /// the entire run, which makes spending one a decision about the whole ladder rather than
    /// the current game.
    /// </summary>
    private const int RetriesPerRun = 3;

    public override void Configure()
    {
        Get("/battle/run");
        AllowAnonymous();
        Description(b => b
            .WithName("GetBattleRun")
            .WithTags("Battle"));

        Summary(s =>
        {
            s.Summary = "Get a day's climb for one monster";
            s.Description =
                "Returns the full ladder: your monster's form in each game, and the three opponents waiting " +
                "there — two ordinary enemies and a boss. All three must be beaten to advance, and your " +
                "monster then becomes that game's version of itself. A loss costs one of the three retries " +
                "that cover the whole run; running out ends it.\n\n" +
                "Battles never cross games, so the wildly different power curves between Final Fantasy and " +
                "Final Fantasy XV never meet. Games where your monster has no form are reported in `skipped` " +
                "rather than dropped silently.\n\n" +
                "The run is derived from the date, so everyone playing on the same day faces the same ladder.\n\n" +
                "Example: `/api/battle/run?family=Bomb`";
            s.Params[nameof(GetRunRequest.Family)] =
                "Required. Monster name from GET /api/battle/starters, e.g. \"Bomb\".";
            s.Params[nameof(GetRunRequest.Date)] =
                "Optional. yyyy-MM-dd. Defaults to today; a future date is rejected.";
            s.Responses[200] = "The run.";
            s.Responses[404] = "No monster by that name can start a run.";
        });
    }

    public override async Task HandleAsync(GetRunRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DailyPuzzle.Today;

        if (DailyPuzzle.IsInFuture(date))
        {
            AddError(r => r.Date, "That run has not been set yet.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var run = await climb.BuildAsync(req.Family, date, ct);
        if (run is null || run.Rungs.Count == 0)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new GetRunResponse(
                run.Family,
                run.Date,
                RetriesPerRun,
                new BattleRules(
                    BattleMath.DamageShare,
                    BattleMath.WeaknessMultiplier,
                    BattleMath.MinRatio,
                    BattleMath.MaxRatio,
                    BattleMath.PoisonShare,
                    BattleMath.BlindMultiplier,
                    BattleMath.StatusTurns),
                run.Rungs.Select(ToRung).ToList(),
                run.Skipped.Select(s => new SkippedRung(s.GameId, s.GameName, s.Reason)).ToList()),
            ct);
    }

    private static RunRung ToRung(BattleRung rung) => new(
        rung.Number,
        rung.GameId,
        rung.GameName,
        ToCombatant(rung.Player),
        rung.Opponents.Select(ToCombatant).ToList());

    private static Combatant ToCombatant(Fighter f) => new(
        f.Id, f.Name, f.GameName, f.Category,
        f.HitPoints, f.Attack, f.Defense, f.MagicAttack, f.MagicDefense, f.Speed,
        ClimbBuilder.SplitList(f.Weaknesses),
        ClimbBuilder.SplitList(f.Absorbs),
        MoveBuilder.Build(f.Abilities)
            .Select(m => new MoveOption(m.Name, m.Element, m.Kind.ToString(), m.Power, m.Recoil, m.Status.ToString()))
            .ToList(),
        f.ImageUrl);
}
