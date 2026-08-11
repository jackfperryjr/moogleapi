using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// What a player may be offered. The draft deals one sphere per game, so the shape that matters is
/// per-game depth — a rule that reads well in aggregate and leaves a game with one eligible monster
/// is a rule that shows that game's single monster every day.
/// </summary>
public class SpherePoolTests
{
    private static Sphere Monster(int id, int gameId, int popularity, int moves = 3, string? name = null) =>
        new(id, name ?? $"m{id}", gameId, $"Game {gameId}", null, null, null,
            new SphereScale.Ratings(55, 55, 55, 55, 55, 55), 40, [], [],
            [.. Enumerable.Range(0, moves).Select(i =>
                new SphereMove($"move{i}", null, MoveCategory.Physical, 60, 100, 0))],
            popularity);

    /// <summary>
    /// The reason this is per-game rather than one popularity line across the library. Notability
    /// in the battle pool has a median of 60, and a global floor falls very unevenly: at 60 Final
    /// Fantasy XII keeps 362 monsters and Final Fantasy III keeps 7.
    /// </summary>
    [Fact]
    public void A_thin_game_keeps_its_full_share_next_to_a_deep_one()
    {
        List<Sphere> pool =
        [
            // A deep bestiary, all of it more notable than anything in the thin one.
            .. Enumerable.Range(1, 300).Select(i => Monster(i, 12, popularity: 70)),
            // A thin one, all of it below any global line that would trim the deep game.
            .. Enumerable.Range(1000, 50).Select(i => Monster(i, 3, popularity: 52)),
        ];

        var draftable = SpherePool.Draftable(pool);

        Assert.Equal(SpherePool.DraftablePerGame, draftable.Count(s => s.GameId == 12));
        Assert.Equal(SpherePool.DraftablePerGame, draftable.Count(s => s.GameId == 3));
    }

    [Fact]
    public void Notability_orders_monsters_that_recur_equally_often()
    {
        List<Sphere> pool = [.. Enumerable.Range(1, 100).Select(i => Monster(i, 6, popularity: i))];

        var draftable = SpherePool.Draftable(pool);

        Assert.Equal(SpherePool.DraftablePerGame, draftable.Count);
        Assert.All(draftable, s => Assert.True(s.Popularity > 100 - SpherePool.DraftablePerGame));
    }

    // ---- recurrence is what "iconic" means -----------------------------------------------------

    /// <summary>
    /// The whole point of ranking on recurrence. Popularity is article length and backlinks, which
    /// rewards whatever the wiki wrote most about — and that is bosses. Ranked on it the pool was
    /// 58% bosses and only 11% monsters appearing in five or more games; it offered Brachioraidos
    /// and missed Coeurl, Flan and Goblin outright.
    /// </summary>
    [Fact]
    public void A_monster_in_many_games_beats_a_better_documented_one_in_a_single_game()
    {
        List<Sphere> pool =
        [
            // A famous recurring monster, thinly written up wherever it appears.
            .. Enumerable.Range(1, 8).Select(i => Monster(100 + i, i, popularity: 40, name: "Tonberry")),
            // And a one-off with a very long article, in the game we are drafting from.
            Monster(1, 1, popularity: 95, name: "Brachioraidos"),
        ];

        var forThatGame = SpherePool.Draftable(pool).Where(s => s.GameId == 1).ToList();

        Assert.Equal("Tonberry", forThatGame[0].Name);
        Assert.Equal("Brachioraidos", forThatGame[1].Name);
    }

    /// <summary>
    /// Excluding bosses was the other candidate and it is the wrong tool: Bahamut is a boss in all
    /// four of its battle-ready forms, as are Ifrit, Omega and Gilgamesh, so the rule that removes
    /// obscure bosses removes the most famous monsters in the series with them. Nothing here may
    /// filter on <c>IsBoss</c>.
    /// </summary>
    [Fact]
    public void A_boss_that_recurs_is_still_draftable()
    {
        List<Sphere> pool =
        [
            .. Enumerable.Range(1, 5).Select(i =>
                new Sphere(200 + i, "Bahamut", i, $"Game {i}", "Boss", null, null,
                    new SphereScale.Ratings(55, 55, 55, 55, 55, 55), 40, [], [],
                    [.. Enumerable.Range(0, 3).Select(m =>
                        new SphereMove($"move{m}", null, MoveCategory.Physical, 60, 100, 0))],
                    50)),
        ];

        Assert.Contains(SpherePool.Draftable(pool), s => s.Name == "Bahamut");
    }

    /// <summary>
    /// Counted across the whole pool, not the draftable part of it. Whether one particular form of
    /// a monster happens to list abilities says nothing about how famous the monster is.
    /// </summary>
    [Fact]
    public void Recurrence_counts_forms_that_are_not_themselves_draftable()
    {
        List<Sphere> pool =
        [
            .. Enumerable.Range(1, 6).Select(i => Monster(300 + i, i, popularity: 10, moves: 2, name: "Bomb")),
            Monster(400, 1, popularity: 10, moves: 3, name: "Bomb"),
            Monster(401, 1, popularity: 90, moves: 3, name: "Somebody Else"),
        ];

        var recurrence = SpherePool.RecurrenceOf(pool);
        Assert.Equal(6, recurrence["Bomb"]);

        // And that count carries: the draftable Bomb outranks the better-documented one-off.
        Assert.Equal("Bomb", SpherePool.Draftable(pool).First(s => s.GameId == 1).Name);
    }

    /// <summary>
    /// Every sphere has an attack and a Limit because both are granted rather than scraped, so a
    /// sphere on exactly two has no abilities on its article at all. Two buttons, one of them
    /// locked behind a gauge, is not a thing to hand a player.
    /// </summary>
    [Fact]
    public void A_sphere_with_no_abilities_of_its_own_is_not_offered()
    {
        List<Sphere> pool =
        [
            Monster(1, 6, popularity: 99, moves: 2),
            Monster(2, 6, popularity: 10, moves: 3),
        ];

        var draftable = SpherePool.Draftable(pool);

        Assert.DoesNotContain(draftable, s => s.Id == 1);
        Assert.Contains(draftable, s => s.Id == 2);
    }

    /// <summary>Whole bestiaries share a notability score, and fortieth place is usually in a tie.</summary>
    [Fact]
    public void A_tie_at_the_cut_resolves_the_same_way_every_time()
    {
        List<Sphere> pool = [.. Enumerable.Range(1, 100).Select(i => Monster(i, 6, popularity: 60))];

        Assert.Equal(
            SpherePool.Draftable(pool).Select(s => s.Id),
            SpherePool.Draftable(pool.AsEnumerable().Reverse()).Select(s => s.Id));
    }

    /// <summary>A game with fewer than the cap contributes everything it has rather than nothing.</summary>
    [Fact]
    public void A_game_below_the_cap_is_not_dropped()
    {
        List<Sphere> pool = [.. Enumerable.Range(1, 5).Select(i => Monster(i, 1, popularity: 50))];

        Assert.Equal(5, SpherePool.Draftable(pool).Count);
    }
}
