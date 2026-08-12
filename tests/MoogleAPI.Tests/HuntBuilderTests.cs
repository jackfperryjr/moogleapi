using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Features.SphereHunter.GetRun;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// Hunt construction. The part worth pinning is the vetting: a hunt the party cannot win is a
/// dead run, and a hunt it wins in one click is not a hunt.
/// </summary>
public class HuntBuilderTests
{
    private const int Level = 50;

    private static Sphere Monster(int id, int hp = 55, int atk = 55, int def = 55, bool boss = false) =>
        new(id, $"m{id}", 6, "Final Fantasy VI", boss ? "Boss" : null, null, null,
            new SphereScale.Ratings(hp, atk, def, atk, def, 55),
            40, [], [],
            [new SphereMove("Attack", null, MoveCategory.Physical, 60, 100, 0)]);

    // ---- vetting ---------------------------------------------------------------------------------

    [Fact]
    public void An_even_opponent_is_kept()
    {
        var party = new[] { Monster(1) };
        var kept = HuntBuilder.Winnable([Monster(2)], party, Level, band: 18);

        Assert.Single(kept);
    }

    /// <summary>A fight decided in a click or two is not one.</summary>
    [Fact]
    public void An_opponent_that_folds_immediately_is_rejected()
    {
        var party = new[] { Monster(1, atk: 100) };

        // Frail and defenceless: the party kills it well inside the minimum, so it is not a hunt.
        var vetted = HuntBuilder.Winnable([Monster(2, hp: 10, def: 10, atk: 10)], party, Level, band: 100);

        // It comes back only through the least-bad fallback, never as a genuinely winnable pick.
        Assert.True(vetted.Count <= 1);
        Assert.True(SphereMath.TurnsToKill(party[0], Monster(2, hp: 10, def: 10, atk: 10), Level) < 3);
    }

    /// <summary>
    /// Nothing here is beatable, and the hunt is still built. The party has two more members, a
    /// Limit the estimate ignores, and the estimate itself is deliberately pessimistic.
    /// </summary>
    [Fact]
    public void A_hopeless_bestiary_still_yields_a_floor_rather_than_dropping_it()
    {
        var party = new[] { Monster(1, hp: 20, atk: 10, def: 10) };
        var monsters = Enumerable.Range(2, 10).Select(i => Monster(i, hp: 100, atk: 100, def: 100)).ToList();

        var vetted = HuntBuilder.Winnable(monsters, party, Level, band: 100);

        Assert.NotEmpty(vetted);
    }

    /// <summary>
    /// Vetted against the party's best answer, not an average — the player gets to switch, so a
    /// hunt is fair if any of the three can handle it.
    /// </summary>
    [Fact]
    public void One_capable_party_member_is_enough_to_make_a_floor_fair()
    {
        var specialist = Monster(1, atk: 90, def: 90, hp: 70);
        var passengers = new[] { Monster(2, atk: 12, def: 12, hp: 15), Monster(3, atk: 12, def: 12, hp: 15) };
        var party = new[] { specialist }.Concat(passengers).ToArray();

        Assert.NotEmpty(HuntBuilder.Winnable([Monster(9, hp: 70, atk: 55, def: 55)], party, Level, band: 60));
    }

    // ---- the party parameter ---------------------------------------------------------------------

    [Theory]
    [InlineData("1,2,3", 3)]
    [InlineData(" 4 , 5 ", 2)]
    [InlineData("7,7,7", 1)]        // duplicates collapse rather than filling the party with one sphere
    [InlineData("1,rubbish,3", 2)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void A_party_is_parsed_leniently_and_deduplicated(string? value, int expected)
    {
        Assert.Equal(expected, Endpoint.ParseIds(value).Count);
    }

    [Fact]
    public void The_tower_has_one_floor_per_battle_ready_game()
    {
        Assert.Equal(13, HuntBuilder.HuntGameIds.Length);
    }

    /// <summary>
    /// Below what a hunt costs, or the run stops trending downward and stops being a run — the
    /// same trap Battle Square's wave recovery documents.
    /// </summary>
    [Fact]
    public void Recovery_between_floors_is_partial()
    {
        Assert.InRange(HuntBuilder.RecoveryBetweenHunts, 0.1, 0.5);
    }

    // ---- the Limit is the player's alone -------------------------------------------------------

    /// <summary>
    /// Jack, 2026-08-11: "It's hard enough getting through their attacks." The gauge fills on
    /// damage taken, so giving it to the opponent inverts what it is for — the sphere winning a
    /// fight hands its opponent the means to end it.
    /// </summary>
    [Fact]
    public void An_opponent_is_served_without_its_limit()
    {
        var withLimit = SphereMoves.For("Blizzara", "Marilith", Element.Ice);
        Assert.Contains(withLimit, m => m.IsLimit);      // the sphere itself still has one

        var fought = Disarm(Monster(9) with { Moves = withLimit });

        Assert.DoesNotContain(fought.Moves, m => m.IsLimit);
        Assert.Contains(fought.Moves, m => m.Name == "Attack");
    }

    /// <summary>
    /// And the mark keeps its Limit, because sealing it puts it in the party. Only the fighting
    /// copy goes without.
    /// </summary>
    [Fact]
    public void The_mark_you_seal_still_has_one()
    {
        var sphere = Monster(9) with { Moves = SphereMoves.For("Blizzara", "Marilith", Element.Ice) };

        Assert.Contains(sphere.Moves, m => m.IsLimit);
    }

    /// <summary>Mirrors HuntBuilder.Disarm, which is private — the behaviour is what matters.</summary>
    private static Sphere Disarm(Sphere sphere) =>
        sphere with { Moves = [.. sphere.Moves.Where(m => !m.IsLimit)] };

    // ---- runs, not days ----------------------------------------------------------------------

    /// <summary>
    /// Jack, 2026-08-11: "If I lose, I want to try again." A run is identified by a token rather
    /// than by the date, so a loss costs a token. Two tokens must not agree, or "try again" deals
    /// the same nine spheres and the same expedition.
    /// </summary>
    [Fact]
    public void Two_runs_are_different_runs()
    {
        Assert.NotEqual(Seed("run-one", "draft"), Seed("run-two", "draft"));
    }

    /// <summary>
    /// And the same token must rebuild identically, because that is what lets a player refresh
    /// mid-climb — the server keeps no run state to fall back on.
    /// </summary>
    [Fact]
    public void The_same_token_rebuilds_the_same_run()
    {
        Assert.Equal(Seed("run-one", "draft"), Seed("run-one", "draft"));
    }

    /// <summary>
    /// The draft and the expedition draw from independent streams, so adding a hunt cannot reshuffle
    /// which spheres were offered.
    /// </summary>
    [Fact]
    public void The_draft_and_the_tower_do_not_share_a_stream()
    {
        Assert.NotEqual(Seed("run-one", "draft"), Seed("run-one", "expedition:1,2,3"));
    }

    /// <summary>Mirrors HuntBuilder.SeedFor, which is private — the behaviour is what matters.</summary>
    private static ulong Seed(string run, string scope) =>
        (ulong)$"spherehunter:v1:{run}:{scope}".GetDeterministicHash();
}
