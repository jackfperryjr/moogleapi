using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Tests;

/// <summary>
/// Offence and guard are divided by each other, so they have to be comparable numbers. They
/// are not the same underlying stat in every game, and these cover the games where the raw
/// figures are on wildly different scales.
/// </summary>
public class BattlePoolTests
{
    private static BattlePool.RawFighter Monster(
        int id, int gameId, int? attack = null, int? defense = null,
        int? magicAttack = null, int? magicDefense = null) =>
        new(id, $"Monster {id}", gameId, "Game", "Enemy", HitPoints: 100,
            attack, defense, magicAttack, magicDefense, Speed: 10,
            Weaknesses: null, Absorbs: null, Abilities: null, ImageUrl: "x.png", Popularity: 50);

    /// <summary>
    /// Final Fantasy XV in miniature: strength in the thousands against a vitality in the
    /// hundreds. Left alone, a median enemy reads as forty times stronger than another median
    /// enemy of the same game.
    /// </summary>
    [Fact]
    public void PutsAGuardStatOnTheSameScaleAsTheOffenceItIsMeasuredAgainst()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 15, attack: 4000, defense: 100),
            Monster(2, 15, attack: 4080, defense: 107),
            Monster(3, 15, attack: 4200, defense: 110),
        ];

        var pool = BattlePool.FillGapsWithGameMedians(raw);
        var median = pool.Single(f => f.Id == 2);

        // The median pairing now sits at parity rather than at a rout.
        Assert.Equal(0.5, BattleMath.Ratio(median.Attack, median.Defense), 2);
    }

    /// <summary>
    /// Final Fantasy VI runs the other way — a small attack against a 0–255 defence — which
    /// pinned every fight at the ratio floor.
    /// </summary>
    [Fact]
    public void LiftsAGuardStatThatSitsFarAboveItsOffence()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 6, attack: 10, defense: 90),
            Monster(2, 6, attack: 13, defense: 110),
            Monster(3, 6, attack: 20, defense: 150),
        ];

        var median = BattlePool.FillGapsWithGameMedians(raw).Single(f => f.Id == 2);

        Assert.Equal(0.5, BattleMath.Ratio(median.Attack, median.Defense), 2);
        Assert.True(BattleMath.Ratio(median.Attack, median.Defense) > BattleMath.MinRatio);
    }

    /// <summary>
    /// Rescaling is monotonic, so it settles where a bestiary sits as a whole without disturbing
    /// which of its monsters is the tougher.
    /// </summary>
    [Fact]
    public void KeepsTheOrderOfAGamesGuardStat()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 15, attack: 4000, defense: 100),
            Monster(2, 15, attack: 4080, defense: 107),
            Monster(3, 15, attack: 4200, defense: 6060),
        ];

        var pool = BattlePool.FillGapsWithGameMedians(raw).OrderBy(f => f.Id).ToList();

        Assert.True(pool[0].Defense < pool[1].Defense);
        Assert.True(pool[1].Defense < pool[2].Defense);
    }

    /// <summary>
    /// A game that publishes no guard stat already borrows the offence median, so there is
    /// nothing to correct and the fighters come out exactly as they did before.
    /// </summary>
    [Fact]
    public void LeavesAGameThatPublishesNoGuardStatAlone()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 15, attack: 4000),
            Monster(2, 15, attack: 4080),
            Monster(3, 15, attack: 4200),
        ];

        var pool = BattlePool.FillGapsWithGameMedians(raw);

        Assert.All(pool, f => Assert.Equal(4080, f.Defense));
    }

    [Fact]
    public void ScalesMagicDefenceAgainstMagicAttackIndependently()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 5, attack: 40, defense: 8, magicAttack: 30, magicDefense: 4),
            Monster(2, 5, attack: 48, defense: 10, magicAttack: 32, magicDefense: 5),
            Monster(3, 5, attack: 50, defense: 12, magicAttack: 34, magicDefense: 6),
        ];

        var median = BattlePool.FillGapsWithGameMedians(raw).Single(f => f.Id == 2);

        Assert.Equal(0.5, BattleMath.Ratio(median.Attack, median.Defense), 2);
        Assert.Equal(0.5, BattleMath.Ratio(median.MagicAttack, median.MagicDefense), 2);
    }

    [Fact]
    public void NeverProducesAGuardOfZero()
    {
        List<BattlePool.RawFighter> raw =
        [
            Monster(1, 6, attack: 1, defense: 200),
            Monster(2, 6, attack: 2, defense: 250),
        ];

        Assert.All(BattlePool.FillGapsWithGameMedians(raw), f => Assert.True(f.Defense >= 1));
    }
}
