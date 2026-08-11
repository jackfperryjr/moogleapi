using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// The rating scale is the change everything else rests on: it is what lets a Final Fantasy Goblin
/// with 8 HP stand in a party beside a Final Fantasy XV Bomb with 5,600, and what lets health stop
/// being decorative.
/// </summary>
public class SphereScaleTests
{
    private static Fighter Monster(int id, int gameId, int hp, int atk = 10, int def = 10,
                                   int mag = 10, int mdf = 10, int spd = 10) =>
        new(id, $"m{id}", gameId, $"Game {gameId}", null, hp, atk, def, mag, mdf, spd,
            null, null, null, null);

    /// <summary>A bestiary whose hit points run 1..count, so any percentile is easy to reason about.</summary>
    private static List<Fighter> Bestiary(int gameId, int count, int scale = 1) =>
        [.. Enumerable.Range(1, count).Select(i => Monster(gameId * 1000 + i, gameId, i * scale))];

    // ---- the point of the whole thing ----------------------------------------------------------

    /// <summary>
    /// Two monsters at the same standing in two wildly different games rate the same. This is the
    /// property that makes a cross-game party possible at all — <see cref="BattlePool"/> refuses to
    /// stage a cross-game fight precisely because raw numbers cannot support one.
    /// </summary>
    [Fact]
    public void The_same_standing_in_two_games_rates_the_same()
    {
        // One bestiary tops out at 100 HP, the other at 100,000. Same shape, different units.
        var scale = SphereScale.Build([.. Bestiary(1, 100), .. Bestiary(2, 100, scale: 1000)]);

        var smallGameTop = scale.For(Monster(0, 1, hp: 100));
        var hugeGameTop = scale.For(Monster(0, 2, hp: 100_000));

        Assert.Equal(smallGameTop.HitPoints, hugeGameTop.HitPoints);
    }

    /// <summary>
    /// Final Fantasy X runs from 696 HP to 2,000,000 because of Penance and the dark aeons. Reading
    /// health off a percentile is only safe because rank does not care how far the tail stretches —
    /// that tail is exactly what made this impossible for Battle Square's invented champions.
    /// </summary>
    [Fact]
    public void A_superboss_does_not_distort_the_rest_of_its_bestiary()
    {
        var ordinary = Bestiary(1, 99);
        var withPenance = new List<Fighter>(ordinary) { Monster(9999, 1, hp: 2_000_000) };

        var median = Monster(0, 1, hp: 50);

        var before = SphereScale.Build(ordinary).For(median).HitPoints;
        var after = SphereScale.Build(withPenance).For(median).HitPoints;

        Assert.InRange(after, before - 1, before + 1);
    }

    // ---- the band ------------------------------------------------------------------------------

    /// <summary>
    /// The weakest and strongest members of a bestiary land near the ends of the band but not
    /// exactly on them — midrank counts a monster as standing half above itself, so only a value
    /// outside the distribution entirely reaches a limit. That is the correct behaviour and worth
    /// stating: the floor is reserved for something genuinely beneath the whole bestiary.
    /// </summary>
    [Fact]
    public void Ratings_stay_inside_the_band()
    {
        var scale = SphereScale.Build(Bestiary(1, 50));

        Assert.InRange(scale.For(Monster(0, 1, hp: 1)).HitPoints, SphereScale.MinRating, SphereScale.MinRating + 2);
        Assert.InRange(scale.For(Monster(0, 1, hp: 50)).HitPoints, SphereScale.MaxRating - 2, SphereScale.MaxRating);

        // Outside the distribution in either direction pins to the end of the band exactly.
        Assert.Equal(SphereScale.MaxRating, scale.For(Monster(0, 1, hp: 999_999)).HitPoints);
        Assert.Equal(SphereScale.MinRating, scale.For(Monster(0, 1, hp: 0)).HitPoints);
    }

    /// <summary>
    /// The floor is 10 rather than 0 or 1 because a rating is a divisor in the damage formula. The
    /// weakest thing in a bestiary should be feeble, not a hole in the arithmetic.
    /// </summary>
    [Fact]
    public void The_floor_is_low_enough_to_be_feeble_and_high_enough_to_divide_by()
    {
        Assert.Equal(10, SphereScale.MinRating);
        Assert.Equal(10, SphereScale.MaxRating / SphereScale.MinRating);
    }

    // ---- midrank -------------------------------------------------------------------------------

    /// <summary>
    /// Whole bestiaries share a value: <see cref="BattlePool.FillGapsWithGameMedians"/> fills every
    /// missing stat with the game's median, so in a game that publishes little, hundreds of
    /// monsters hold the median at once. Counting strictly-below dumps them all at the bottom of
    /// the band; counting at-or-below sends them all to the top.
    /// </summary>
    [Fact]
    public void A_value_shared_by_most_of_the_bestiary_lands_mid_band()
    {
        // Ninety monsters on the median, five below, five above.
        List<Fighter> pool =
        [
            .. Enumerable.Range(0, 5).Select(i => Monster(i, 1, hp: 1)),
            .. Enumerable.Range(5, 90).Select(i => Monster(i, 1, hp: 50)),
            .. Enumerable.Range(95, 5).Select(i => Monster(i, 1, hp: 100)),
        ];

        var rating = SphereScale.Build(pool).For(Monster(0, 1, hp: 50)).HitPoints;

        Assert.InRange(rating, 45, 65);
    }

    [Fact]
    public void Percentile_splits_a_tie_down_the_middle()
    {
        int[] sorted = [5, 5, 5, 5];

        Assert.Equal(0.5, SphereScale.Percentile(sorted, 5));
        Assert.Equal(0.0, SphereScale.Percentile(sorted, 1));
        Assert.Equal(1.0, SphereScale.Percentile(sorted, 9));
    }

    // ---- health --------------------------------------------------------------------------------

    [Fact]
    public void Health_rating_becomes_a_real_pool_of_hit_points()
    {
        var ratings = new SphereScale.Ratings(HitPoints: 55, 50, 50, 50, 50, 50);

        Assert.Equal((int)Math.Round(55 * SphereScale.HealthPerRating),
                     ratings.HealthAt(SphereScale.ReferenceLevel));
    }

    /// <summary>
    /// Health is quoted at the reference level and scaled from there, because damage grows with
    /// level too and the two are meant to cancel.
    /// </summary>
    [Fact]
    public void Health_scales_linearly_with_level()
    {
        var ratings = new SphereScale.Ratings(HitPoints: 55, 50, 50, 50, 50, 50);

        Assert.Equal(ratings.HealthAt(SphereScale.ReferenceLevel) * 2,
                     ratings.HealthAt(SphereScale.ReferenceLevel * 2), tolerance: 1);
    }

    /// <summary>A game the pool has never heard of rates mid-band rather than throwing.</summary>
    [Fact]
    public void An_unknown_game_rates_everything_in_the_middle()
    {
        var ratings = SphereScale.Build(Bestiary(1, 20)).For(Monster(0, gameId: 77, hp: 5));

        Assert.Equal(55, ratings.HitPoints);
        Assert.Equal(55, ratings.Speed);
    }
}
