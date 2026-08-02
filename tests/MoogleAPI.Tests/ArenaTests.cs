using MoogleAPI.Web.Infrastructure.Arena;
using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Tests;

/// <summary>
/// Job, weapon and ability strings are taken verbatim from scraped character rows, so these
/// fail if the wiki's infobox wording drifts away from what the reader expects.
/// </summary>
public class ArchetypeTests
{
    [Theory]
    [InlineData("Black Mage", Archetype.Mage)]
    [InlineData("White Mage/Ranger", Archetype.Mage)]
    [InlineData("Summoner", Archetype.Mage)]
    [InlineData("Knight", Archetype.Warrior)]
    [InlineData("Dragoon", Archetype.Warrior)]
    [InlineData("Monk", Archetype.Scout)]
    [InlineData("Sky Pirate", Archetype.Scout)]
    public void ReadsTheJobWhenTheArticleStatesOne(string job, Archetype expected) =>
        Assert.Equal(expected, ArchetypeReader.For(job, weapon: null, abilities: null));

    [Theory]
    [InlineData("Shuriken", Archetype.Scout)]
    [InlineData("Rifle", Archetype.Scout)]
    [InlineData("Blitzballs", Archetype.Scout)]
    [InlineData("Masamune", Archetype.Warrior)]
    [InlineData("Broadswords", Archetype.Warrior)]
    [InlineData("Staves", Archetype.Mage)]
    [InlineData("Dolls", Archetype.Mage)]
    [InlineData("Knuckles", Archetype.Scout)]
    [InlineData("Firearms", Archetype.Scout)]
    [InlineData("Katana", Archetype.Warrior)]
    [InlineData("Greatswords", Archetype.Warrior)]
    public void FallsBackToTheWeaponWhenThereIsNoJob(string weapon, Archetype expected) =>
        Assert.Equal(expected, ArchetypeReader.For(job: null, weapon, abilities: null));

    /// <summary>
    /// About half the roster has no job — the job-system games have no fixed class to record —
    /// so the weapon has to carry them. Reading the whole field against each pattern in turn
    /// made Terra a Scout on the strength of the daggers she is listed as being able to hold.
    /// </summary>
    [Theory]
    [InlineData("Most swords, daggers", Archetype.Warrior)]
    [InlineData("Bows, staves", Archetype.Scout)]
    [InlineData("Rods, daggers", Archetype.Mage)]
    public void TakesTheWeaponTheArticleListsFirst(string weapon, Archetype expected) =>
        Assert.Equal(expected, ArchetypeReader.For(job: null, weapon, abilities: null));

    [Theory]
    [InlineData("Blk Mag, Focus", Archetype.Mage)]
    [InlineData("Steal, Skill, Dyne", Archetype.Scout)]
    [InlineData("Swd Art, Swd Mag", Archetype.Warrior)]
    public void FallsBackToAbilitiesWhenNeitherJobNorWeaponIsRecorded(string abilities, Archetype expected) =>
        Assert.Equal(expected, ArchetypeReader.For(job: null, weapon: null, abilities));

    /// <summary>
    /// Final Fantasy I, III, V and XV publish none of the three for most of their cast, so the
    /// fallback has to be a real archetype rather than a failure.
    /// </summary>
    [Fact]
    public void FallsBackToBalancedWhenTheArticleSaysNothingUseful() =>
        Assert.Equal(Archetype.Balanced, ArchetypeReader.For(null, null, null));

    /// <summary>
    /// The occupation field is never consulted, and this is why: in this series it describes the
    /// day job, not the fighting style. Aerith's is "Florist" and Tifa's "Bar hostess".
    /// </summary>
    [Fact]
    public void DoesNotReadAnOccupationAsAFightingStyle() =>
        Assert.Equal(Archetype.Balanced, ArchetypeReader.For(job: null, weapon: null, abilities: "Florist"));

    /// <summary>
    /// No archetype may be strictly better than another, or the roster screen has one right
    /// answer. Each one's gains have to be paid for somewhere.
    /// </summary>
    [Theory]
    [InlineData(Archetype.Warrior)]
    [InlineData(Archetype.Mage)]
    [InlineData(Archetype.Scout)]
    public void EveryArchetypeGivesUpAsMuchAsItGains(Archetype archetype)
    {
        var w = ArchetypeReader.WeightsFor(archetype);
        var total = w.HitPoints + w.Attack + w.Defense + w.MagicAttack + w.MagicDefense + w.Speed;

        Assert.InRange(total, 5.9, 6.1);
    }
}

public class LevelCurveTests
{
    [Fact]
    public void RisesWithLevel()
    {
        for (var level = LevelCurve.MinLevel; level < LevelCurve.MaxLevel; level++)
            Assert.True(LevelCurve.PercentileFor(level + 1) > LevelCurve.PercentileFor(level),
                $"level {level + 1} must be worth more than {level}");
    }

    /// <summary>
    /// Level 1 sits above the floor and level 99 short of the ceiling. A character starting
    /// below every enemy in the game reads as broken, and one capped at the superboss leaves
    /// the last waves with nothing to threaten them.
    /// </summary>
    [Fact]
    public void StaysInsideItsBand()
    {
        Assert.InRange(LevelCurve.PercentileFor(LevelCurve.MinLevel), 0.15, 0.25);
        Assert.InRange(LevelCurve.PercentileFor(LevelCurve.MaxLevel), 0.90, 0.99);
    }

    [Fact]
    public void ClampsLevelsOutsideTheRange()
    {
        Assert.Equal(LevelCurve.PercentileFor(LevelCurve.MinLevel), LevelCurve.PercentileFor(-40));
        Assert.Equal(LevelCurve.PercentileFor(LevelCurve.MaxLevel), LevelCurve.PercentileFor(500));
    }

    /// <summary>
    /// The curve is front-loaded like the series' own, so the early levels are worth more than
    /// the late ones. Without it levels 1-30 are nearly indistinguishable and the level
    /// recommendation has no resolution in the range it usually lands in.
    /// </summary>
    [Fact]
    public void IsFrontLoaded()
    {
        var early = LevelCurve.PercentileFor(25) - LevelCurve.PercentileFor(1);
        var late = LevelCurve.PercentileFor(99) - LevelCurve.PercentileFor(75);

        Assert.True(early > late, $"early gain {early:F3} should beat late gain {late:F3}");
    }
}

public class GameStatScaleTests
{
    private static List<Fighter> Pool(params int[] hitPoints) =>
        hitPoints.Select((hp, i) => Fighter(i, hp)).ToList();

    private static Fighter Fighter(int id, int hp) =>
        new(id, $"M{id}", 7, "Final Fantasy VII", "Enemy", hp, 10, 10, 10, 10, 10, null, null, null, null);

    [Fact]
    public void ReadsAPercentileOffTheGamesOwnNumbers()
    {
        var scale = GameStatScale.For(Pool(10, 20, 30, 40, 50))!;

        Assert.Equal(10, scale.HitPointsAt(0));
        Assert.Equal(30, scale.HitPointsAt(0.5));
        Assert.Equal(50, scale.HitPointsAt(1));
    }

    /// <summary>
    /// Nearest-rank would quantise the level curve to the number of monsters in the game,
    /// landing several levels on the same value and producing stats that cost the player a
    /// level and change nothing.
    /// </summary>
    [Fact]
    public void InterpolatesBetweenSamples() =>
        Assert.Equal(15, GameStatScale.For(Pool(10, 20))!.HitPointsAt(0.5));

    [Fact]
    public void HasNoScaleForAGameWithNoBattleReadyMonsters() =>
        Assert.Null(GameStatScale.For([]));

    /// <summary>
    /// The whole reason this class exists: the series has no shared scale, so the same
    /// percentile has to mean 8 HP in one game and thousands in another.
    /// </summary>
    [Fact]
    public void QuotesEachGameInItsOwnUnits()
    {
        var nes = GameStatScale.For(Pool(8, 12, 20, 34, 80))!;
        var modern = GameStatScale.For(Pool(5600, 9000, 24000, 60000, 300000))!;

        Assert.True(nes.HitPointsAt(0.5) < 100);
        Assert.True(modern.HitPointsAt(0.5) > 10000);
    }
}

public class ChampionBuilderTests
{
    private static readonly List<Fighter> GamePool =
        Enumerable.Range(1, 40)
            .Select(i => new Fighter(i, $"M{i}", 7, "Final Fantasy VII", "Enemy",
                i * 100, i * 5, i * 5, i * 5, i * 5, i * 5, null, null, null, null))
            .ToList();

    private static ArenaCharacter Character(string? job = null, string? weapon = null, string? abilities = null) =>
        new(1, "Test", 7, "Final Fantasy VII", job, weapon, abilities, null, 90);

    [Fact]
    public void EveryStatRisesWithLevel()
    {
        var scale = GameStatScale.For(GamePool)!;

        var low = ChampionBuilder.Build(Character(), 5, scale, "Final Fantasy VII");
        var high = ChampionBuilder.Build(Character(), 90, scale, "Final Fantasy VII");

        Assert.True(high.HitPoints > low.HitPoints);
        Assert.True(high.Attack > low.Attack);
        Assert.True(high.MagicAttack > low.MagicAttack);
        Assert.True(high.Defense > low.Defense);
    }

    [Fact]
    public void AMageOutguesssAWarriorAtMagicAndLosesAtSteel()
    {
        var scale = GameStatScale.For(GamePool)!;

        var mage = ChampionBuilder.Build(Character(job: "Black Mage"), 50, scale, "Final Fantasy VII");
        var warrior = ChampionBuilder.Build(Character(job: "Knight"), 50, scale, "Final Fantasy VII");

        Assert.True(mage.MagicAttack > warrior.MagicAttack);
        Assert.True(warrior.Attack > mage.Attack);
    }

    /// <summary>
    /// A party member's elemental affinity is not published anywhere, and inventing one would be
    /// the single most damaging guess available: a weakness doubles incoming damage, so it would
    /// put a whole run on a coin flip nothing in the data supports.
    /// </summary>
    [Fact]
    public void NeverInventsAnElementalAffinity()
    {
        var champion = ChampionBuilder.Build(Character(), 50, GameStatScale.For(GamePool)!, "Final Fantasy VII");

        Assert.Null(champion.Weaknesses);
        Assert.Null(champion.Absorbs);
    }

    [Fact]
    public void MarksTheChampionAsACharacterRatherThanAnEncounter()
    {
        var champion = ChampionBuilder.Build(Character(), 50, GameStatScale.For(GamePool)!, "Final Fantasy VII");

        Assert.Equal(ChampionBuilder.ChampionCategory, champion.Category);
        Assert.False(champion.IsBoss);
    }

    /// <summary>
    /// Final Fantasy I, III, V and XV publish no character abilities at all, so a third of the
    /// roster would arrive with nothing but Attack — a fight with no decisions in it, when
    /// elemental choice is the only decision this combat model has.
    /// </summary>
    [Fact]
    public void GivesACharacterWithNoScrapedAbilitiesSomethingToChooseBetween()
    {
        var moves = ChampionBuilder.MovesFor(Character(weapon: "Swords"), Archetype.Warrior);

        Assert.True(moves.Count > 1);
        Assert.Contains(moves, m => m.Element is not null);
    }

    [Fact]
    public void AMageCarriesEnoughElementsToAnswerAnyWeakness()
    {
        var moves = ChampionBuilder.MovesFor(Character(job: "Black Mage"), Archetype.Mage);

        Assert.True(moves.Where(m => m.Element is not null).Select(m => m.Element).Distinct().Count() >= 3);
    }

    [Fact]
    public void KeepsTheCharactersOwnCommandsAheadOfTheStockKit()
    {
        var moves = ChampionBuilder.MovesFor(Character(abilities: "Braver, Cross-slash"), Archetype.Warrior);

        Assert.Contains(moves, m => m.Name == "Braver");
        Assert.Contains(moves, m => m.Name == "Cross-slash");
    }

    /// <summary>Four buttons is the one advantage the player has over a wave picked to match them.</summary>
    [Fact]
    public void NeverHandsOutMoreButtonsThanTheBarHolds()
    {
        var moves = ChampionBuilder.MovesFor(
            Character(abilities: "Braver, Cross-slash, Blade Beam, Climhazzard, Meteorain", weapon: "Rods"),
            Archetype.Mage);

        Assert.True(moves.Count <= 4);
    }

    [Fact]
    public void DoesNotOfferTheSameMoveTwice()
    {
        var moves = ChampionBuilder.MovesFor(Character(abilities: "Fire"), Archetype.Mage);

        Assert.Equal(moves.Select(m => m.Name.ToLowerInvariant()).Distinct().Count(), moves.Count);
    }
}

public class HandicapTests
{
    /// <summary>
    /// A run that opens on "Materia broken" is decided before the player has made a single
    /// choice, so the reel never spins before the first wave.
    /// </summary>
    [Fact]
    public void TheFirstWaveIsNeverHandicapped() =>
        Assert.Equal(HandicapKind.None, HandicapReel.For(seed: 12345, waveNumber: 1).Kind);

    [Fact]
    public void TheSameSeedAndWaveAlwaysDrawTheSameHandicap()
    {
        for (var wave = 2; wave <= ArenaBuilder.WavesPerRun; wave++)
            Assert.Equal(HandicapReel.For(99, wave).Kind, HandicapReel.For(99, wave).Kind);
    }

    [Fact]
    public void DifferentWavesDoNotAllDrawTheSameThing()
    {
        var drawn = Enumerable.Range(2, 7).Select(w => HandicapReel.For(4242, w).Kind).Distinct();

        Assert.True(drawn.Count() > 1);
    }

    /// <summary>A handicap that paid nothing would just be a tax on bad luck.</summary>
    [Fact]
    public void EveryHandicapPaysMoreThanNoHandicap()
    {
        foreach (var handicap in HandicapReel.All.Where(h => h.Kind != HandicapKind.None))
            Assert.True(handicap.Multiplier > HandicapReel.None.Multiplier,
                $"{handicap.Name} must be worth more than an empty reel");
    }

    [Fact]
    public void OnlyTheConditionHandicapsCarryAStatus()
    {
        foreach (var handicap in HandicapReel.All)
        {
            var expected = handicap.Kind is HandicapKind.Blind or HandicapKind.Poison or HandicapKind.Silence;
            Assert.Equal(expected, handicap.Status != StatusEffect.None);
        }
    }

    /// <summary>
    /// The reel may only take away what a player can still play around. Anything that ends the
    /// run on the spot is an unannounced loss, not a handicap.
    /// </summary>
    [Fact]
    public void EveryHandicapLeavesTheRunPlayable()
    {
        foreach (var handicap in HandicapReel.All)
        {
            Assert.NotEqual("", handicap.Name);
            Assert.NotEqual("", handicap.Description);
            Assert.InRange(handicap.Multiplier, 1.0, 3.0);
        }
    }
}

public class ArenaShapeTests
{
    /// <summary>
    /// The Battle Square runs the games with enough published enemy stats to hold a fight, which
    /// is the same list the climb uses — a character has to be able to meet monsters from their
    /// own game, and the reasons a game has none of those are not game-specific.
    /// </summary>
    [Fact]
    public void RunsTheSameGamesTheClimbDoes() =>
        Assert.Equal(ClimbBuilder.LadderGameIds, BattlePool.GameIds);

    [Fact]
    public void IsEightWavesLikeTheOriginal() =>
        Assert.Equal(8, ArenaBuilder.WavesPerRun);

    /// <summary>
    /// Recovery has to be real, or the format is arithmetically impossible: damage is 30% of the
    /// defender's maximum HP with the ratio clamped at 0.8, so no fight can cost its winner less
    /// than about 23% of their health and eight in a row cannot total under ~1.9. It also has to
    /// stay below what a wave costs, or health never trends down and the run stops being one.
    /// </summary>
    [Fact]
    public void RecoversLessThanAWaveCosts() =>
        Assert.InRange(ArenaBuilder.WaveRecovery, 0.05, 0.30);
}

/// <summary>
/// The roster is cached, and HybridCache serializes whatever it is handed.
/// </summary>
/// <remarks>
/// This is a regression test with a specific failure behind it: the roster originally cached the
/// EF <c>Character</c> entity, loaded with its <c>Game</c> navigation. Serializing one walks
/// <c>Character → Game → Characters → Game</c> until <c>System.Text.Json</c> gives up at depth 64,
/// so <c>GET /api/arena/roster</c> answered 400 on every call — and nothing caught it, because a
/// cycle only exists once the entity is attached to a real context. Anything the arena caches has
/// to be plain values.
/// </remarks>
public class ArenaCacheTests
{
    [Fact]
    public void ThePlayableCastSerializesWithoutCycling()
    {
        var characters = new List<ArenaCharacter>
        {
            new(1, "Cloud Strife", 7, "Final Fantasy VII", null, "Swords", "Braver", "https://x/1.webp", 100),
            new(2, "Vivi Ornitier", 9, "Final Fantasy IX", "Black Mage", "Staves", "Blk Mag, Focus", null, 89),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(characters);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<ArenaCharacter>>(json);

        Assert.Equal(characters, back);
    }

    [Fact]
    public void TheRosterSerializesWithoutCycling()
    {
        var roster = new List<RosterEntry>
        {
            new(1, "Cloud Strife", 7, "Final Fantasy VII", Archetype.Warrior, null, "Swords", null, 100, 55),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(roster);

        Assert.Equal(roster, System.Text.Json.JsonSerializer.Deserialize<List<RosterEntry>>(json));
    }

    /// <summary>
    /// The guard that would have caught it: nothing the arena caches may carry an entity, and an
    /// entity is recognisable by living in the Models namespace.
    /// </summary>
    [Fact]
    public void NothingTheArenaCachesCarriesADatabaseEntity()
    {
        foreach (var type in new[] { typeof(ArenaCharacter), typeof(RosterEntry) })
            Assert.DoesNotContain(type.GetProperties(), p =>
                p.PropertyType.Namespace == typeof(MoogleAPI.Web.Infrastructure.Models.Character).Namespace);
    }
}
