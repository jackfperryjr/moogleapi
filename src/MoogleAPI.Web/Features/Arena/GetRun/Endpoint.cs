using FastEndpoints;
using MoogleAPI.Web.Infrastructure.Arena;
using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Arena.GetRun;

/// <remarks>
/// The whole run ships in one response — the champion, all eight waves, both sides' moves and
/// the damage model. Combat then resolves in the browser with no further calls, the same way
/// the climb works, which keeps a full run inside the anonymous rate limit.
/// </remarks>
public class Endpoint(ArenaBuilder arena) : Endpoint<GetArenaRunRequest, GetArenaRunResponse>
{
    public override void Configure()
    {
        Get("/arena/run");
        AllowAnonymous();
        Description(b => b
            .WithName("GetArenaRun")
            .WithTags("Arena"));

        Summary(s =>
        {
            s.Summary = "Get a day's eight waves for one character";
            s.Description =
                "The Gold Saucer's Battle Square, opened up to the whole series: one character against " +
                "eight consecutive waves of their own game's monsters, ending on a boss. Nothing heals " +
                "between waves, and between each one the slots take something away — darkness, silence, " +
                "half your health, every move but Attack. A harsher handicap pays more battle points.\n\n" +
                "Levels are positions in the character's own game's stat distribution rather than absolute " +
                "numbers, because the series has no shared scale: a Final Fantasy Goblin has 8 HP where a " +
                "Final Fantasy XV Bomb has 5,600. Level 40 places a character above the same share of their " +
                "game's monsters everywhere.\n\n" +
                "Waves are chosen against the character who has to fight them — a mage and a warrior in the " +
                "same game get different ladders, because a fight is only as hard as it is for you. The date " +
                "fixes the draw, so the same character on the same day always meets the same eight.\n\n" +
                "Example: `/api/arena/run?characterId=245&level=35`";
            s.Params[nameof(GetArenaRunRequest.CharacterId)] =
                "Required. From GET /api/arena/roster.";
            s.Params[nameof(GetArenaRunRequest.Level)] =
                "Optional, 1-99. Defaults to the character's recommended level for the day.";
            s.Params[nameof(GetArenaRunRequest.Date)] =
                "Optional. yyyy-MM-dd. Defaults to today; a future date is rejected.";
            s.Responses[200] = "The run.";
            s.Responses[404] = "No playable character with that id, or their game cannot field eight waves.";
        });
    }

    public override async Task HandleAsync(GetArenaRunRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DailyPuzzle.Today;

        if (DailyPuzzle.IsInFuture(date))
        {
            AddError(r => r.Date, "That run has not been set yet.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var run = await arena.BuildAsync(req.CharacterId, req.Level, date, ct);
        if (run is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new GetArenaRunResponse(
                new ChampionEntry(
                    run.Champion.CharacterId,
                    run.Champion.Name,
                    run.Champion.Archetype.ToString(),
                    run.Champion.Job,
                    run.Champion.Weapon,
                    run.Champion.Level,
                    run.RecommendedLevel,
                    ToCombatant(run.Champion.Fighter, run.Champion.Moves)),
                run.GameId,
                run.GameName,
                run.Date,
                new BattleRules(
                    BattleMath.DamageShare,
                    BattleMath.WeaknessMultiplier,
                    BattleMath.MinRatio,
                    BattleMath.MaxRatio,
                    BattleMath.PoisonShare,
                    BattleMath.BlindMultiplier,
                    BattleMath.StatusTurns,
                    HandicapReel.StatPenalty),
                run.Waves.Select(ToWave).ToList()),
            ct);
    }

    private static ArenaWave ToWave(Wave wave) => new(
        wave.Number,
        ToCombatant(wave.Opponent, MoveBuilder.Build(wave.Opponent.Abilities)),
        new HandicapOption(
            wave.Handicap.Kind.ToString(),
            wave.Handicap.Name,
            wave.Handicap.Description,
            wave.Handicap.Status.ToString(),
            wave.Handicap.Multiplier),
        wave.BattlePoints,
        Math.Round(wave.Cost, 4));

    /// <remarks>
    /// Moves are passed in rather than rebuilt from <see cref="Fighter.Abilities"/>. A champion's
    /// list is their scraped commands plus their archetype's kit, and deriving it again here
    /// would drop the kit — handing the browser a different move bar than the one the server
    /// picked the waves against.
    /// </remarks>
    private static Combatant ToCombatant(Fighter f, IReadOnlyList<Move> moves) => new(
        f.Id, f.Name, f.GameName, f.Category,
        f.HitPoints, f.Attack, f.Defense, f.MagicAttack, f.MagicDefense, f.Speed,
        ClimbBuilder.SplitList(f.Weaknesses),
        ClimbBuilder.SplitList(f.Absorbs),
        moves
            .Select(m => new MoveOption(m.Name, m.Element, m.Kind.ToString(), m.Power, m.Recoil, m.Status.ToString()))
            .ToList(),
        f.ImageUrl);
}
