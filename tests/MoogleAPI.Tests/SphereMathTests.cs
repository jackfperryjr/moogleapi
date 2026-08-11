using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// The combat model. Two things here are load-bearing beyond ordinary correctness: that health
/// finally matters, which is the entire reason the rating scale exists, and that a battle replays
/// identically from its seed, which is what allows the model to have dice at all.
/// </summary>
public class SphereMathTests
{
    private const int Level = 50;

    /// <summary>One per battle-ready game — see <see cref="BattlePool.GameIds"/>.</summary>
    private const int Floors = 11;

    private static Sphere Fighter(
        int hp = 55, int atk = 55, int def = 55, int mag = 55, int mdf = 55, int spd = 55,
        Element? affinity = null, Element[]? weak = null, Element[]? absorbs = null,
        IReadOnlyList<SphereMove>? moves = null)
    {
        var ratings = new SphereScale.Ratings(hp, atk, def, mag, mdf, spd);

        return new Sphere(
            1, "Test", 6, "Final Fantasy VI", null, null, affinity, ratings,
            SphereMoves.MagicPointsFor(mag), weak ?? [], absorbs ?? [],
            moves ?? [Physical()]);
    }

    private static SphereMove Physical(int power = 60, Element? element = null, int accuracy = 100) =>
        new("Attack", element, MoveCategory.Physical, power, accuracy, 0);

    private static SphereMove Magic(int power = 60, Element? element = null) =>
        new("Spell", element, MoveCategory.Magic, power, 100, 5);

    // ---- the headline: health is a real stat now -----------------------------------------------

    /// <summary>
    /// The old model took damage as a share of the <em>defender's own</em> maximum health, so
    /// raising health raised incoming damage by exactly as much and bulk was decorative. Doubling
    /// the health rating must now roughly double how long a sphere survives, or the rating scale
    /// bought nothing.
    /// </summary>
    [Fact]
    public void Doubling_health_roughly_doubles_how_long_a_sphere_lasts()
    {
        var attacker = Fighter();
        var frail = Fighter(hp: 30);
        var bulky = Fighter(hp: 60);

        var frailTurns = SphereMath.TurnsToKill(attacker, frail, Level);
        var bulkyTurns = SphereMath.TurnsToKill(attacker, bulky, Level);

        Assert.InRange(bulkyTurns / (double)frailTurns, 1.7, 2.3);
    }

    /// <summary>
    /// The same assertion stated against the old model, so the regression is impossible to
    /// reintroduce quietly: under share-of-max-HP these two numbers were identical.
    /// </summary>
    [Fact]
    public void Health_does_not_cancel_out_the_way_it_used_to()
    {
        var attacker = Fighter();

        Assert.NotEqual(
            SphereMath.TurnsToKill(attacker, Fighter(hp: 20), Level),
            SphereMath.TurnsToKill(attacker, Fighter(hp: 90), Level));
    }

    /// <summary>
    /// The constant that ties the rating scale to the damage formula. An evenly matched pair should
    /// need a handful of hits — a fight decided in one is not one, and fifteen is a grind.
    /// </summary>
    [Fact]
    public void An_even_matchup_lasts_a_handful_of_turns()
    {
        var turns = SphereMath.TurnsToKill(Fighter(), Fighter(), Level);

        Assert.InRange(turns, 4, 8);
    }

    /// <summary>And the extremes stay inside something playable rather than exploding either way.</summary>
    [Theory]
    [InlineData(10, 100, 1, 4)]     // the frailest thing in a bestiary, hit by the strongest
    [InlineData(100, 10, 15, 60)]   // the bulkiest, hit by the feeblest
    public void The_widest_mismatches_stay_bounded(int hp, int atk, int lower, int upper)
    {
        var turns = SphereMath.TurnsToKill(Fighter(atk: atk), Fighter(hp: hp, def: 55), Level);

        Assert.InRange(turns, lower, upper);
    }

    /// <summary>
    /// The one this model got wrong first time, and the mistake is invisible at any single level.
    /// </summary>
    /// <remarks>
    /// Damage carries a <c>(2 × level / 5 + 2)</c> term and grows roughly tenfold across the tower.
    /// With health held fixed that made the ground floor a twenty-turn slog and the top floor a
    /// three-turn blitz — difficulty running backwards. Health scales with level for exactly this
    /// reason, and the whole climb has to sit inside a couple of turns of itself.
    /// </remarks>
    [Fact]
    public void A_fight_is_about_as_long_on_every_floor_of_the_tower()
    {
        var lengths = Enumerable.Range(1, Floors)
            .Select(floor => SphereMath.TurnsToKill(Fighter(), Fighter(), SphereFactory.LevelForFloor(floor, Floors)))
            .ToList();

        Assert.InRange(lengths.Min(), 3, 8);
        Assert.InRange(lengths.Max(), 3, 8);
        Assert.True(lengths.Max() - lengths.Min() <= 2,
                    $"fight length drifts across the tower: {string.Join(", ", lengths)}");
    }

    [Fact]
    public void Health_grows_with_level()
    {
        var sphere = Fighter(hp: 55);

        Assert.True(sphere.HealthAt(SphereFactory.MaxLevel) > sphere.HealthAt(SphereFactory.MinLevel) * 3);
    }

    // ---- effectiveness -------------------------------------------------------------------------

    /// <summary>
    /// Published data wins outright. Compounding a real weakness with the grid's opinion of the
    /// same pairing would land on quadruple damage off one fact and one guess.
    /// </summary>
    [Fact]
    public void A_published_weakness_does_not_compound_with_the_grid()
    {
        // Weak to Fire per the article, and Ice by affinity — which the grid would also call
        // super-effective against Fire. It must be 2x, not 4x.
        var defender = Fighter(affinity: Element.Ice, weak: [Element.Fire]);

        Assert.Equal(Elements.SuperEffective, SphereMath.Effectiveness(defender, Element.Fire));
    }

    [Fact]
    public void Absorbing_beats_a_published_weakness()
    {
        var defender = Fighter(weak: [Element.Fire], absorbs: [Element.Fire]);

        Assert.Equal(Elements.Absorbed, SphereMath.Effectiveness(defender, Element.Fire));
    }

    /// <summary>The grid supplies the only not-very-effective tier the wiki data cannot.</summary>
    [Fact]
    public void The_grid_fills_the_silence()
    {
        var fireMonster = Fighter(affinity: Element.Fire);

        Assert.Equal(Elements.NotVeryEffective, SphereMath.Effectiveness(fireMonster, Element.Fire));
        Assert.Equal(Elements.SuperEffective, SphereMath.Effectiveness(fireMonster, Element.Ice));
        Assert.Equal(Elements.Neutral, SphereMath.Effectiveness(fireMonster, Element.Holy));
    }

    [Fact]
    public void A_non_elemental_move_is_always_neutral()
    {
        var defender = Fighter(affinity: Element.Fire, weak: [Element.Ice]);

        Assert.Equal(Elements.Neutral, SphereMath.Effectiveness(defender, null));
    }

    // ---- affinity ------------------------------------------------------------------------------

    [Fact]
    public void A_sphere_hits_harder_with_its_own_element()
    {
        var caster = Fighter(affinity: Element.Fire);

        Assert.Equal(SphereMath.AffinityBonus, SphereMath.Affinity(caster, Magic(element: Element.Fire)));
        Assert.Equal(1.0, SphereMath.Affinity(caster, Magic(element: Element.Ice)));
        Assert.Equal(1.0, SphereMath.Affinity(caster, Magic(element: null)));
    }

    // ---- status --------------------------------------------------------------------------------

    /// <summary>Both of these attack physical damage, and neither should touch a spell.</summary>
    [Theory]
    [InlineData(Status.Blind)]
    [InlineData(Status.Sap)]
    public void Blind_and_sap_blunt_physical_moves_only(Status status)
    {
        var attacker = Fighter();
        var defender = Fighter();

        var physical = SphereMath.Deterministic(attacker, defender, Physical(), Level, status);
        var healthy = SphereMath.Deterministic(attacker, defender, Physical(), Level, Status.None);
        var spell = SphereMath.Deterministic(attacker, defender, Magic(), Level, status);
        var spellHealthy = SphereMath.Deterministic(attacker, defender, Magic(), Level, Status.None);

        Assert.True(physical < healthy);
        Assert.Equal(spellHealthy, spell);
    }

    /// <summary>Poison compounds so that leaving it up becomes the reason to switch; sap does not.</summary>
    [Fact]
    public void Poison_compounds_and_sap_is_flat()
    {
        var sphere = Fighter();

        Assert.True(SphereMath.TickDamage(sphere, Status.Poison, 3, Level)
                  > SphereMath.TickDamage(sphere, Status.Poison, 1, Level));

        Assert.Equal(
            SphereMath.TickDamage(sphere, Status.Sap, 1, Level),
            SphereMath.TickDamage(sphere, Status.Sap, 3, Level));
    }

    [Fact]
    public void Only_poison_and_sap_bleed()
    {
        var sphere = Fighter();

        foreach (var status in new[] { Status.None, Status.Blind, Status.Silence, Status.Sleep, Status.Paralyze })
            Assert.Equal(0, SphereMath.TickDamage(sphere, status, 1, Level));
    }

    [Fact]
    public void Paralyze_halves_speed_and_nothing_else_touches_it()
    {
        var sphere = Fighter(spd: 80);

        Assert.Equal(40, SphereMath.Speed(sphere, Status.Paralyze));
        Assert.Equal(80, SphereMath.Speed(sphere, Status.Sleep));
        Assert.Equal(80, SphereMath.Speed(sphere, Status.None));
    }

    // ---- the dice ------------------------------------------------------------------------------

    /// <summary>
    /// The whole reason dice are allowed here. Sleep and Paralyze were banned from the old model
    /// because it had no randomness; they are safe now only because a battle replays exactly.
    /// </summary>
    [Fact]
    public void The_same_seed_resolves_to_the_same_strike()
    {
        var attacker = Fighter();
        var defender = Fighter();

        var first = SphereMath.Resolve(attacker, defender, Physical(accuracy: 80), Level, Status.None,
                                       DeterministicRandom.ForScope(42, "floor", 3));
        var second = SphereMath.Resolve(attacker, defender, Physical(accuracy: 80), Level, Status.None,
                                        DeterministicRandom.ForScope(42, "floor", 3));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A miss must consume as much of the stream as a hit, or every later roll depends on whether
    /// an earlier one landed and a client replaying from the seed diverges at the first
    /// disagreement.
    /// </summary>
    [Fact]
    public void A_miss_and_a_hit_leave_the_stream_in_the_same_place()
    {
        var attacker = Fighter();
        var defender = Fighter();

        static int NextAfter(Sphere a, Sphere d, int accuracy)
        {
            var rng = DeterministicRandom.ForScope(7, "floor", 1);
            SphereMath.Resolve(a, d, Physical(accuracy: accuracy), Level, Status.None, rng);
            return rng.Next(1_000_000);
        }

        Assert.Equal(NextAfter(attacker, defender, 100), NextAfter(attacker, defender, 0));
    }

    [Fact]
    public void Damage_never_lands_outside_the_variance_band()
    {
        var attacker = Fighter();
        var defender = Fighter();
        var move = Physical();

        var nominal = SphereMath.Deterministic(attacker, defender, move, Level, Status.None);

        for (var seed = 0; seed < 300; seed++)
        {
            var strike = SphereMath.Resolve(attacker, defender, move, Level, Status.None,
                                            DeterministicRandom.ForScope((ulong)seed, "roll"));

            if (strike.Missed) continue;

            // The ceiling allows for a critical on top of the top of the variance band.
            Assert.InRange(strike.Damage, (int)(nominal * SphereMath.MinVariance) - 1,
                                          (int)(nominal * SphereMath.CriticalMultiplier) + 1);
        }
    }

    /// <summary>Absorbing is not dodging — the move lands and does nothing, and says so.</summary>
    [Fact]
    public void An_absorbed_move_is_reported_as_absorbed_rather_than_missed()
    {
        var strike = SphereMath.Resolve(
            Fighter(), Fighter(absorbs: [Element.Fire]), Magic(element: Element.Fire), Level,
            Status.None, DeterministicRandom.ForScope(1, "x"));

        Assert.Equal(0, strike.Damage);
        Assert.False(strike.Missed);
        Assert.Equal(Elements.Absorbed, strike.Effectiveness);
    }

    // ---- the limit gauge -------------------------------------------------------------------------

    /// <summary>
    /// Roughly seventy per cent of a sphere's health fills the gauge. Much faster and the Limit is
    /// just a fourth move; much slower and it only ever fires on something about to faint.
    /// </summary>
    [Fact]
    public void Losing_most_of_your_health_fills_the_gauge()
    {
        var sphere = Fighter(hp: 55);
        var mostOfIt = (int)(sphere.HealthAt(Level) * 0.72);

        Assert.True(SphereMath.LimitGained(sphere, mostOfIt, Level) >= SphereMath.LimitFull);
        Assert.True(SphereMath.LimitGained(sphere, sphere.HealthAt(Level) / 4, Level) < SphereMath.LimitFull);
    }

    [Fact]
    public void The_limit_never_misses_and_costs_nothing()
    {
        var limit = SphereMoves.Limit("Bahamut", Element.Dark);

        Assert.Equal(100, limit.Accuracy);
        Assert.Equal(0, limit.MpCost);
        Assert.True(limit.IsLimit);
        Assert.Equal(Element.Dark, limit.Element);
    }

    /// <summary>
    /// Excluded from the estimate because it fires once and the gauge may never fill — counting it
    /// would rate a floor as winnable on a resource the player might not get.
    /// </summary>
    [Fact]
    public void The_estimate_ignores_the_limit_and_the_suicide_move()
    {
        var withExtras = Fighter(moves:
        [
            Physical(power: 20),
            SphereMoves.Limit("Test", null),
            new SphereMove("Self-Destruct", null, MoveCategory.Magic, 156, 100, 0, Recoil: 0.5),
        ]);

        var plain = Fighter(moves: [Physical(power: 20)]);
        var defender = Fighter();

        Assert.Equal(
            SphereMath.TurnsToKill(plain, defender, Level),
            SphereMath.TurnsToKill(withExtras, defender, Level));
    }

    // ---- levels ----------------------------------------------------------------------------------

    [Fact]
    public void The_tower_climbs_from_five_to_eighty()
    {
        Assert.Equal(SphereFactory.MinLevel, SphereFactory.LevelForFloor(1, Floors));
        Assert.Equal(SphereFactory.MaxLevel, SphereFactory.LevelForFloor(Floors, Floors));

        // And monotonically in between, so a floor is never easier than the one below it.
        var levels = Enumerable.Range(1, Floors).Select(f => SphereFactory.LevelForFloor(f, Floors)).ToList();
        Assert.Equal(levels.OrderBy(l => l), levels);
    }

    [Fact]
    public void A_higher_floor_hits_harder()
    {
        var attacker = Fighter();
        var defender = Fighter();

        var low = SphereMath.Deterministic(attacker, defender, Physical(), SphereFactory.LevelForFloor(1, Floors), Status.None);
        var high = SphereMath.Deterministic(attacker, defender, Physical(), SphereFactory.LevelForFloor(Floors, Floors), Status.None);

        Assert.True(high > low * 2);
    }

    // ---- moves -------------------------------------------------------------------------------------

    /// <summary>
    /// The fallback a sphere is left with when its magic is spent, so it cannot be the one that
    /// misses — and self-destruct already pays for its power in health.
    /// </summary>
    [Fact]
    public void The_basic_attack_and_the_suicide_move_always_land()
    {
        var built = SphereMoves.For("Self-Destruct, Blizzara", "Bomb", Element.Fire);

        Assert.Equal(100, built.Single(m => m.Name == "Attack").Accuracy);
        Assert.Equal(100, built.Single(m => m.Name.Contains("Destruct")).Accuracy);
    }

    [Fact]
    public void Stronger_moves_are_less_reliable()
    {
        var weak = SphereMoves.Convert(new Move("Jab", null, MoveKind.Physical, 0.9));
        var strong = SphereMoves.Convert(new Move("Flare", "Fire", MoveKind.Magic, 1.3));

        Assert.True(strong.Accuracy < weak.Accuracy);
        Assert.InRange(strong.Accuracy, 75, 100);
    }

    /// <summary>Physical moves are free, which is what keeps an empty pool from being a dead end.</summary>
    [Fact]
    public void Only_magic_costs_the_pool()
    {
        Assert.Equal(0, SphereMoves.Convert(new Move("Jab", null, MoveKind.Physical, 1.0)).MpCost);
        Assert.True(SphereMoves.Convert(new Move("Fira", "Fire", MoveKind.Magic, 1.3)).MpCost > 0);
    }

    [Fact]
    public void Every_sphere_gets_a_limit_and_an_attack()
    {
        var built = SphereMoves.For(null, "Goblin", null);

        Assert.Contains(built, m => m.Name == "Attack");
        Assert.Contains(built, m => m.IsLimit);
    }
}
