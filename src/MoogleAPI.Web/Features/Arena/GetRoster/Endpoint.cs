using FastEndpoints;
using MoogleAPI.Web.Infrastructure.Arena;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Arena.GetRoster;

public class Endpoint(ArenaBuilder arena) : Endpoint<GetRosterRequest, GetRosterResponse>
{
    public override void Configure()
    {
        Get("/arena/roster");
        AllowAnonymous();
        Description(b => b
            .WithName("GetArenaRoster")
            .WithTags("Arena"));

        Summary(s =>
        {
            s.Summary = "List the characters that can enter the Battle Square";
            s.Description =
                "The playable cast of every game with enough published enemy stats to hold a fight — " +
                "Cloud, Terra, Yuna, Lightning and the rest. A character is here when the wiki lists them " +
                "in their game's playable group, so villains, summons and shopkeepers stay out.\n\n" +
                "`archetype` is worked out from the character's job, weapon and abilities, and decides how " +
                "their level is spent: a Mage trades physical damage for magic, a Scout trades both for " +
                "speed. `recommendedLevel` is solved against today's actual waves rather than looked up.\n\n" +
                "Pass `characterId` to `GET /api/arena/run`.\n\n" +
                "Example: `/api/arena/roster?gameId=7`";
            s.Params[nameof(GetRosterRequest.GameId)] = "Optional. Restrict the roster to one game.";
            s.Responses[200] = "The roster, by game then popularity.";
        });
    }

    public override async Task HandleAsync(GetRosterRequest req, CancellationToken ct)
    {
        // Today's, always. The recommended level is solved against a particular day's waves, so
        // the roster has to be quoting the same day the run will be built for.
        var roster = await arena.GetRosterAsync(req.GameId, DailyPuzzle.Today, ct);

        var characters = roster
            .Select(r => new RosterCharacter(
                r.CharacterId, r.Name, r.GameId, r.GameName, r.Archetype.ToString(),
                r.Job, r.Weapon, r.ImageUrl, r.Popularity, r.RecommendedLevel))
            .ToList();

        // Listed from the returned roster rather than the games table, so a game only appears
        // when it actually has characters to offer — a picker built from this can't open on an
        // empty game.
        var games = roster
            .GroupBy(r => (r.GameId, r.GameName))
            .OrderBy(g => g.Key.GameId)
            .Select(g => new RosterGame(g.Key.GameId, g.Key.GameName, g.Count()))
            .ToList();

        await Send.OkAsync(new GetRosterResponse(characters, games), ct);
    }
}
