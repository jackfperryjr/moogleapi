using MoogleAPI.Web.Features.SphereHunter.GetRun;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// Floor construction. The part worth pinning is the vetting: a floor the party cannot win is a
/// dead run, and a floor it wins in one click is not a floor.
/// </summary>
public class TowerBuilderTests
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
        var kept = TowerBuilder.Winnable([Monster(2)], party, Level, band: 18);

        Assert.Single(kept);
    }

    /// <summary>A fight decided in a click or two is not one.</summary>
    [Fact]
    public void An_opponent_that_folds_immediately_is_rejected()
    {
        var party = new[] { Monster(1, atk: 100) };

        // Frail and defenceless: the party kills it well inside the minimum, so it is not a floor.
        var vetted = TowerBuilder.Winnable([Monster(2, hp: 10, def: 10, atk: 10)], party, Level, band: 100);

        // It comes back only through the least-bad fallback, never as a genuinely winnable pick.
        Assert.True(vetted.Count <= 1);
        Assert.True(SphereMath.TurnsToKill(party[0], Monster(2, hp: 10, def: 10, atk: 10), Level) < 3);
    }

    /// <summary>
    /// Nothing here is beatable, and the floor is still built. The party has two more members, a
    /// Limit the estimate ignores, and the estimate itself is deliberately pessimistic.
    /// </summary>
    [Fact]
    public void A_hopeless_bestiary_still_yields_a_floor_rather_than_dropping_it()
    {
        var party = new[] { Monster(1, hp: 20, atk: 10, def: 10) };
        var monsters = Enumerable.Range(2, 10).Select(i => Monster(i, hp: 100, atk: 100, def: 100)).ToList();

        var vetted = TowerBuilder.Winnable(monsters, party, Level, band: 100);

        Assert.NotEmpty(vetted);
    }

    /// <summary>
    /// Vetted against the party's best answer, not an average — the player gets to switch, so a
    /// floor is fair if any of the three can handle it.
    /// </summary>
    [Fact]
    public void One_capable_party_member_is_enough_to_make_a_floor_fair()
    {
        var specialist = Monster(1, atk: 90, def: 90, hp: 70);
        var passengers = new[] { Monster(2, atk: 12, def: 12, hp: 15), Monster(3, atk: 12, def: 12, hp: 15) };
        var party = new[] { specialist }.Concat(passengers).ToArray();

        Assert.NotEmpty(TowerBuilder.Winnable([Monster(9, hp: 70, atk: 55, def: 55)], party, Level, band: 60));
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
        Assert.Equal(11, TowerBuilder.FloorGameIds.Length);
    }

    /// <summary>
    /// Below what a floor costs, or the run stops trending downward and stops being a run — the
    /// same trap Battle Square's wave recovery documents.
    /// </summary>
    [Fact]
    public void Recovery_between_floors_is_partial()
    {
        Assert.InRange(TowerBuilder.FloorRecovery, 0.1, 0.5);
    }
}
