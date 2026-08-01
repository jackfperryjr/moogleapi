using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Tests;

/// <summary>
/// Ability names are taken verbatim from scraped monster rows, so these fail if the wiki's
/// naming drifts away from what the element inference expects.
/// </summary>
public class MoveBuilderTests
{
    [Theory]
    [InlineData("Blaze", "Fire")]
    [InlineData("Firaga", "Fire")]
    [InlineData("Blizzara", "Ice")]
    [InlineData("Thundaga", "Thunder")]
    [InlineData("Bolt", "Thunder")]
    [InlineData("Aqua Breath", "Water")]
    [InlineData("Quake", "Earth")]
    [InlineData("Aero", "Wind")]
    [InlineData("Holy", "Holy")]
    [InlineData("Shadow Flare", "Dark")]
    public void InfersTheElementFromTheAbilityName(string ability, string expected)
    {
        Assert.Equal(expected, MoveBuilder.ElementFor(ability));
    }

    [Theory]
    [InlineData("Bodyblow")]
    [InlineData("Rush")]
    [InlineData("Tail Screw")]
    public void LeavesUnrecognizedAbilitiesNonElemental(string ability)
    {
        Assert.Null(MoveBuilder.ElementFor(ability));
    }

    [Fact]
    public void EveryMonsterCanAlwaysAttack()
    {
        // Plenty of scraped rows have no abilities at all; they still have to be able to fight.
        var moves = MoveBuilder.Build(null);

        Assert.Single(moves);
        Assert.Equal("Attack", moves[0].Name);
    }

    [Fact]
    public void BuildsOneMovePerAbilityUpToTheCap()
    {
        var moves = MoveBuilder.Build("Blaze, Self-Destruct, Fira, Tail, Bite, Scratch");

        // Attack plus at most three abilities — a row of buttons, not a spreadsheet.
        Assert.Equal(4, moves.Count);
        Assert.Equal("Attack", moves[0].Name);
    }

    [Fact]
    public void ElementalAbilitiesBecomeMagicAndHitHarderThanABasicAttack()
    {
        var blaze = MoveBuilder.Build("Blaze")[1];

        Assert.Equal("Fire", blaze.Element);
        Assert.Equal(MoveKind.Magic, blaze.Kind);
        Assert.True(blaze.Power > 1.0);
        Assert.Equal(0, blaze.Recoil);
    }

    /// <summary>
    /// Self-Destruct is the Bomb family's identity. It has to be worth choosing and worth
    /// hesitating over, so it hits hardest and costs the user half its health.
    /// </summary>
    [Theory]
    [InlineData("Self-Destruct")]
    [InlineData("Self Destruct")]
    [InlineData("Exploder")]
    [InlineData("Bomb Blast")]
    public void SelfDestructCostsTheUserHalfItsHealth(string ability)
    {
        var move = MoveBuilder.Build(ability)[1];

        Assert.Equal(0.5, move.Recoil);
        Assert.True(move.Power > 2.0, $"expected a heavy hit, got {move.Power}");
    }

    [Fact]
    public void StripsTheArticlesParentheticalNotesFromMoveNames()
    {
        // Straight from the FFVI Bomb: "| snes special attack = Hit (Level 1 = Attack x 1.5)"
        var moves = MoveBuilder.Build("Hit (Level 1 = Attack x 1.5)");

        Assert.Equal("Hit", moves[1].Name);
    }

    [Fact]
    public void DoesNotOfferTheSameMoveTwice()
    {
        var moves = MoveBuilder.Build("Blaze, blaze, BLAZE");

        Assert.Equal(2, moves.Count);
    }

    // Verbatim from the scraped row for Gilgamesh (Final Fantasy V): the wiki transcribes the
    // enemy's AI script, so stage directions sit among the real moves.
    private const string GilgameshAbilities =
        "!Attack, Flee, Jump, Wind Slash, Electrocute, Aera, Goblin Punch, Protect, Haste, Shell, " +
        "Attack (clone of !Attack), Unhide enemy, Flip sprite horizontally, Unnamed script trigger, " +
        "Death Claw, Missile, Rocket Punch, Pond's Chorus, Hurricane, Self-Destruct";

    [Fact]
    public void KeepsScriptBookkeepingOffTheButtons()
    {
        var names = MoveBuilder.Build(GilgameshAbilities).Select(m => m.Name).ToList();

        Assert.DoesNotContain("Unhide enemy", names);
        Assert.DoesNotContain("Flip sprite horizontally", names);
        Assert.DoesNotContain("Unnamed script trigger", names);
        Assert.DoesNotContain(names, n => n.Contains("clone of", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DoesNotHandOutTwoAttackButtons()
    {
        // "!Attack" is the same move as the basic attack every combatant already has.
        var names = MoveBuilder.Build(GilgameshAbilities).Select(m => m.Name).ToList();

        Assert.Single(names, n => n.Equals("Attack", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Three slots against twenty-odd abilities: the elemental ones have to win, because they
    /// are what turns a matchup into a decision. Article order would give Gilgamesh "Flee".
    /// </summary>
    [Fact]
    public void PrefersElementalAndSelfDestructOverFlavourMoves()
    {
        var moves = MoveBuilder.Build(GilgameshAbilities);
        var names = moves.Select(m => m.Name).ToList();

        Assert.DoesNotContain("Flee", names);
        Assert.DoesNotContain("Jump", names);
        Assert.Contains("Self-Destruct", names);

        // With this many elemental options, every slot beyond the basic attack should be one.
        Assert.All(moves.Skip(1), m =>
            Assert.True(m.Element is not null || m.Recoil > 0, $"'{m.Name}' took a slot from an elemental move"));
    }

    /// <summary>
    /// Verbatim from the scraped FFII rows. Final Fantasy II tags a spell level onto the name
    /// and lists both the numeral and the NES translation, so one move arrives three times.
    /// </summary>
    [Theory]
    [InlineData("Self Destruct VII, Self Destruct 7, Explode7")]
    [InlineData("Self-Destruct III, Self-Destruct 3, Explode3")]
    [InlineData("Self-Destruct V, Self-Destruct 5, Explode5")]
    public void CollapsesOneMoveWrittenThreeWaysIntoOneButton(string abilities)
    {
        var moves = MoveBuilder.Build(abilities);

        Assert.Equal(2, moves.Count);                  // the basic attack, plus one suicide move
        Assert.Equal("Attack", moves[0].Name);
        Assert.Equal(0.5, moves[1].Recoil);
        Assert.DoesNotContain(moves, m => m.Name.Any(char.IsDigit));
    }

    [Theory]
    [InlineData("Blizzara II", "Blizzara")]
    [InlineData("Aero2", "Aero")]
    [InlineData("Fire 3", "Fire")]
    public void StripsSpellLevelsFromMoveNames(string ability, string expected)
    {
        Assert.Equal(expected, MoveBuilder.Build(ability)[1].Name);
    }

    [Theory]
    [InlineData("Magic")]     // ends in C
    [InlineData("Drill")]     // ends in L
    [InlineData("Mix")]       // ends in X
    public void DoesNotMistakeTrailingLettersForRomanNumerals(string ability)
    {
        Assert.Equal(ability, MoveBuilder.Build(ability)[1].Name);
    }

    /// <summary>
    /// Straight from the live run: a Final Fantasy IV Deathmask listed Reflect among its
    /// abilities, the builder made it a 1.15-power attack, and because the picker takes the
    /// strongest option it hit Ifrit for 19,320 a turn with a move that heals nobody.
    /// </summary>
    [Fact]
    public void NeverTurnsAStatusBuffIntoAnAttack()
    {
        var names = MoveBuilder.Build("Holy, Bio, Reflect").Select(m => m.Name).ToList();

        Assert.DoesNotContain("Reflect", names);
        Assert.Contains("Holy", names);
        Assert.Contains("Bio", names);
    }

    [Theory]
    [InlineData("Protect")]
    [InlineData("Shell")]
    [InlineData("Haste")]
    [InlineData("Mighty Guard")]
    [InlineData("Barrier Change")]
    [InlineData("Cure")]
    [InlineData("Curaga")]
    [InlineData("Healara")]
    [InlineData("White Wind")]
    [InlineData("Flee")]
    public void ExcludesSupportAndRestorativeMoves(string ability)
    {
        var moves = MoveBuilder.Build(ability);

        Assert.Single(moves);                    // only the basic attack survives
        Assert.Equal("Attack", moves[0].Name);
    }

    [Theory]
    [InlineData("Curse")]        // not a cure
    [InlineData("Holy")]         // restorative-sounding, but damage
    [InlineData("Blizzaga")]
    [InlineData("Aera")]
    public void KeepsDamagingMovesWithSimilarNames(string ability)
    {
        Assert.Equal(2, MoveBuilder.Build(ability).Count);
    }

    [Fact]
    public void OffersAtMostOneSuicideMove()
    {
        var moves = MoveBuilder.Build("Self-Destruct, Exploder, Bomb Blast, Blaze");

        Assert.Single(moves, m => m.Recoil > 0);
        Assert.Contains(moves, m => m.Element == "Fire");
    }

    [Fact]
    public void StillFillsTheSlotsWhenNothingIsElemental()
    {
        var names = MoveBuilder.Build("Bodyblow, Tail Screw, Rush, Charge").Select(m => m.Name).ToList();

        Assert.Equal(4, names.Count);           // Attack plus three
        Assert.Contains("Bodyblow", names);
    }

    [Theory]
    [InlineData("Poison", StatusEffect.Poison)]
    [InlineData("Venom", StatusEffect.Poison)]
    [InlineData("Bio", StatusEffect.Poison)]
    [InlineData("Bad Breath", StatusEffect.Poison)]
    [InlineData("Blind", StatusEffect.Blind)]
    [InlineData("Sandstorm", StatusEffect.Blind)]
    [InlineData("Ink", StatusEffect.Blind)]
    [InlineData("Silence", StatusEffect.Silence)]
    [InlineData("Mute", StatusEffect.Silence)]
    public void InfersTheStatusFromTheAbilityName(string ability, StatusEffect expected)
    {
        Assert.Equal(expected, MoveBuilder.StatusFor(ability));
    }

    /// <summary>
    /// The Dark element pattern already claims "dark", "shadow" and "doom". Folding Final Fantasy
    /// IV's blinding "Darkness" into the Blind patterns would have taken all of them with it and
    /// quietly made every shadow-flavoured move in the series blinding.
    /// </summary>
    [Theory]
    [InlineData("Darkness")]
    [InlineData("Shadow Flare")]
    [InlineData("Doom")]
    [InlineData("Attack")]
    [InlineData("Blizzaga")]
    public void LeavesOrdinaryMovesFreeOfStatus(string ability)
    {
        Assert.Equal(StatusEffect.None, MoveBuilder.StatusFor(ability));
    }

    [Fact]
    public void AStatusMoveGivesUpPowerToInflict()
    {
        var plain = MoveBuilder.Build("Bodyblow").Single(m => m.Name == "Bodyblow");
        var poisons = MoveBuilder.Build("Poison Sting").Single(m => m.Name == "Poison Sting");

        Assert.Equal(StatusEffect.None, plain.Status);
        Assert.Equal(StatusEffect.Poison, poisons.Status);
        Assert.True(poisons.Power < plain.Power);
    }

    /// <summary>
    /// Silence locks out Magic moves, so a monster whose whole kit is silenced would have nothing
    /// to press. The basic Attack every combatant is handed is Physical, which is what guarantees
    /// a turn is never lost — the client's foe picker leans on it directly.
    /// </summary>
    [Fact]
    public void EveryMonsterKeepsAPhysicalMoveThroughSilence()
    {
        var moves = MoveBuilder.Build("Firaga, Blizzaga, Thundaga");

        Assert.Contains(moves, m => m.Kind == MoveKind.Physical);
    }

    /// <summary>
    /// Three buttons for twenty abilities means the ordering decides what a monster actually is.
    /// A status move has to outrank plain flavour damage or the enemy side of the feature is
    /// never seen — an FFI Piscodemon offering "Bodyblow, Rush, Charge" over "Silence" is a
    /// worse fight than the same monster with the condition on the board.
    /// </summary>
    [Fact]
    public void PrefersStatusMovesOverPlainFlavourDamage()
    {
        var names = MoveBuilder.Build("Bodyblow, Rush, Charge, Silence, Blaze")
            .Select(m => m.Name).ToList();

        Assert.Contains("Blaze", names);     // elemental still outranks everything
        Assert.Contains("Silence", names);
        Assert.DoesNotContain("Charge", names);
    }
}

public class DeterministicRandomTests
{
    [Fact]
    public void TheSameSeedAlwaysProducesTheSameSequence()
    {
        var first = new DeterministicRandom(12345);
        var second = new DeterministicRandom(12345);

        for (var i = 0; i < 50; i++)
            Assert.Equal(first.Next(1000), second.Next(1000));
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);

        var drawsA = Enumerable.Range(0, 20).Select(_ => a.Next(1000)).ToList();
        var drawsB = Enumerable.Range(0, 20).Select(_ => b.Next(1000)).ToList();

        Assert.NotEqual(drawsA, drawsB);
    }

    /// <summary>
    /// Each rung draws from its own stream, so changing how many opponents rung 1 picks can
    /// never shift rung 2's line-up — a run shared yesterday still replays the same today.
    /// </summary>
    [Fact]
    public void ScopedStreamsAreIndependentAndReproducible()
    {
        var rung1 = DeterministicRandom.ForScope(999, "rung", 1);
        var rung2 = DeterministicRandom.ForScope(999, "rung", 2);
        var rung1Again = DeterministicRandom.ForScope(999, "rung", 1);

        var first = Enumerable.Range(0, 10).Select(_ => rung1.Next(500)).ToList();
        var second = Enumerable.Range(0, 10).Select(_ => rung2.Next(500)).ToList();
        var repeat = Enumerable.Range(0, 10).Select(_ => rung1Again.Next(500)).ToList();

        Assert.Equal(first, repeat);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void StaysInsideTheRequestedBound()
    {
        var rng = new DeterministicRandom(42);

        for (var i = 0; i < 200; i++)
            Assert.InRange(rng.Next(7), 0, 6);
    }

    [Fact]
    public void HandlesAnEmptyRange()
    {
        Assert.Equal(0, new DeterministicRandom(1).Next(0));
    }
}

/// <summary>
/// Stats here are the real scraped values for the Final Fantasy IV rung that exposed the
/// problem: a Deathmask has half Ifrit's health — comfortably inside the HP band — but
/// twenty-six times its defence, and beat it in four turns.
/// </summary>
public class BattleMathTests
{
    private static Fighter Ifrit() => new(
        Id: 1, Name: "Ifrit", GameId: 4, GameName: "Final Fantasy IV", Category: "Boss",
        HitPoints: 70000, Attack: 177, Defense: 5, MagicAttack: 36, MagicDefense: 44, Speed: 36,
        Weaknesses: null, Absorbs: "Fire", Abilities: "Flame, Flame Thrower, Firaga", ImageUrl: "x");

    private static Fighter Deathmask() => new(
        Id: 2, Name: "Deathmask", GameId: 4, GameName: "Final Fantasy IV", Category: "Boss",
        HitPoints: 37000, Attack: 49, Defense: 131, MagicAttack: 33, MagicDefense: 75, Speed: 62,
        Weaknesses: null, Absorbs: null, Abilities: "Holy, Bio, Flare", ImageUrl: "x");

    private static IReadOnlyList<Move> MovesOf(Fighter f) => MoveBuilder.Build(f.Abilities);

    [Fact]
    public void RatioIsClampedSoNothingIsOneShotOrUnhittable()
    {
        // Ifrit's defence of 5 against a 177 attack would otherwise be a 0.97 ratio.
        Assert.Equal(BattleMath.MaxRatio, BattleMath.Ratio(177, 5));
        Assert.Equal(BattleMath.MinRatio, BattleMath.Ratio(1, 999));
        Assert.InRange(BattleMath.Ratio(50, 50), BattleMath.MinRatio, BattleMath.MaxRatio);
    }

    [Fact]
    public void AnAbsorbedMoveDoesNoDamage()
    {
        var fire = new Move("Firaga", "Fire", MoveKind.Magic, 1.3);

        // Ifrit absorbs Fire, so hitting it with Firaga is worse than useless.
        Assert.Equal(0, BattleMath.DamagePerHit(Deathmask(), Ifrit(), fire));
    }

    [Fact]
    public void AWeaknessDoublesDamage()
    {
        var weak = Ifrit() with { Weaknesses = "Ice", Absorbs = null };
        var ice = new Move("Blizzaga", "Ice", MoveKind.Magic, 1.3);
        var plain = new Move("Blizzaga", null, MoveKind.Magic, 1.3);

        Assert.Equal(
            BattleMath.DamagePerHit(Deathmask(), weak, plain) * BattleMath.WeaknessMultiplier,
            BattleMath.DamagePerHit(Deathmask(), weak, ice),
            3);
    }

    /// <summary>
    /// Self-destruct can win a fight but can't be the plan for one, so it must not make a
    /// hopeless matchup look survivable.
    /// </summary>
    [Fact]
    public void TurnsToKillIgnoresSelfDestruct()
    {
        var withoutIt = Ifrit() with { Abilities = "Flame" };
        var withIt = Ifrit() with { Abilities = "Flame, Self-Destruct" };

        Assert.Equal(
            BattleMath.TurnsToKill(withoutIt, MovesOf(withoutIt), Deathmask()),
            BattleMath.TurnsToKill(withIt, MovesOf(withIt), Deathmask()));
    }

    [Fact]
    public void RecognisesTheMatchupThatStartedThis()
    {
        var ifrit = Ifrit();
        var deathmask = Deathmask();

        var ifritNeeds = BattleMath.TurnsToKill(ifrit, MovesOf(ifrit), deathmask);
        var deathmaskNeeds = BattleMath.TurnsToKill(deathmask, MovesOf(deathmask), ifrit);

        // The player needing more turns than the opponent is exactly what a losing fight is,
        // and it is the condition the run builder now rejects a candidate on.
        Assert.True(ifritNeeds > deathmaskNeeds,
            $"expected Ifrit to be outmatched: it needs {ifritNeeds} turns, Deathmask {deathmaskNeeds}");
    }

    /// <summary>
    /// Ifrit absorbs Fire, so an attacker whose only ability is Firaga has nothing that lands —
    /// except the plain attack every combatant carries. That's what stops a matchup from being
    /// unresolvable, and why a stalemate can't be reached by picking the wrong element.
    /// </summary>
    [Fact]
    public void TheBasicAttackAlwaysLeavesAWayToFinishAFight()
    {
        var onlyFire = Deathmask() with { Abilities = "Firaga" };
        var turns = BattleMath.TurnsToKill(onlyFire, MovesOf(onlyFire), Ifrit());

        Assert.NotEqual(int.MaxValue, turns);
        Assert.InRange(turns, 1, 500);
    }
}

public class LadderTests
{
    /// <summary>
    /// The ladder deliberately omits VIII, XI, XIV and XVI: their enemy articles publish no
    /// usable HP, so no battle can be staged there. If a future scrape fills those in, this
    /// test is the reminder to reconsider the list.
    /// </summary>
    [Fact]
    public void SkipsTheGamesWithoutPublishedEnemyStats()
    {
        Assert.DoesNotContain(8, ClimbBuilder.LadderGameIds);
        Assert.DoesNotContain(11, ClimbBuilder.LadderGameIds);
        Assert.DoesNotContain(14, ClimbBuilder.LadderGameIds);
        Assert.DoesNotContain(16, ClimbBuilder.LadderGameIds);
    }

    /// <summary>
    /// II is excluded on playability rather than missing stats: 1 of its 166 battle-ready
    /// monsters carries an elemental weakness, so nothing there rewards choosing a move.
    /// </summary>
    [Fact]
    public void SkipsTheGameWithNoElementalCounterplay()
    {
        Assert.DoesNotContain(2, ClimbBuilder.LadderGameIds);
    }

    [Fact]
    public void RunsInReleaseOrder()
    {
        Assert.Equal(ClimbBuilder.LadderGameIds.OrderBy(id => id), ClimbBuilder.LadderGameIds);
    }
}
