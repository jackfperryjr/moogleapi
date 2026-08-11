using MoogleAPI.Web.Infrastructure.Wiki;

namespace MoogleAPI.Tests;

/// <summary>
/// Fixtures are verbatim excerpts of live Final Fantasy Wiki articles, so these tests fail
/// if the wiki's infobox layout drifts away from what the parsers expect.
/// </summary>
public class CardParsingTests
{
    private const string GeezardInfobox = """
        {{infobox equipment
        |name=Geezard
        |release=FFVIII
        |image=<gallery>
        Geezard Card from FFVIII Remastered.png|Remastered
        TTGeezard.png|PlayStation
        </gallery>
        |type=[[File:Triple Triad card icon from FFVIII.png|15px]][[Final Fantasy VIII Triple Triad cards|Triple Triad card]]<br/>Level 1 Monster Card
        |stats=1<br/>5 4<br/>1
        |element=None
        }}
        """;

    private const string SquallInfobox = """
        {{infobox equipment
        |name=Squall
        |type=[[File:Triple Triad card icon from FFVIII.png|15px]][[Final Fantasy VIII Triple Triad cards|Triple Triad card]]<br/>Level 10 Player Card
        |stats=A<br/>9 4<br/>6
        |element=None
        }}
        """;

    private const string IfritInfobox = """
        {{infobox equipment
        |name=Ifrit
        |type=[[File:Triple Triad card icon from FFVIII.png|15px]][[Final Fantasy VIII Triple Triad cards|Triple Triad card]]<br/>Level 8 GF Card
        |stats=9<br/>8 6<br/>2
        |element=[[File:Tripletriad-fire.png]] Fire
        }}
        """;

    [Fact]
    public void ParsesCornerValuesInTopLeftRightBottomOrder()
    {
        var card = WikiClient.ParseCard(GeezardInfobox);

        Assert.NotNull(card);
        Assert.Equal(1, card.Top);
        Assert.Equal(5, card.Left);
        Assert.Equal(4, card.Right);
        Assert.Equal(1, card.Bottom);
    }

    [Fact]
    public void TranslatesAceNotationToTen()
    {
        var card = WikiClient.ParseCard(SquallInfobox);

        Assert.NotNull(card);
        Assert.Equal(10, card.Top);
        Assert.Equal(9, card.Left);
        Assert.Equal(4, card.Right);
        Assert.Equal(6, card.Bottom);
    }

    [Fact]
    public void ReadsLevelAndClassFromTypeField()
    {
        Assert.Equal(1, WikiClient.ParseCard(GeezardInfobox)!.Level);
        Assert.Equal("Monster", WikiClient.ParseCard(GeezardInfobox)!.CardClass);

        Assert.Equal(10, WikiClient.ParseCard(SquallInfobox)!.Level);
        Assert.Equal("Player", WikiClient.ParseCard(SquallInfobox)!.CardClass);

        Assert.Equal(8, WikiClient.ParseCard(IfritInfobox)!.Level);
        Assert.Equal("GF", WikiClient.ParseCard(IfritInfobox)!.CardClass);
    }

    [Fact]
    public void StripsElementIconAndNormalizesNoneToNull()
    {
        Assert.Equal("Fire", WikiClient.ParseCard(IfritInfobox)!.Element);
        Assert.Null(WikiClient.ParseCard(GeezardInfobox)!.Element);
    }

    [Fact]
    public void ReturnsNullWhenStatsAreMissing()
    {
        Assert.Null(WikiClient.ParseCard("{{infobox equipment\n|name=Not a card\n}}"));
    }

    [Fact]
    public void ParsesCardListEntries()
    {
        const string listPage = """
            !{{LA|Geezard (Final Fantasy VIII card)|Geezard}}
            |[[File:TTGeezard.png|120px|Geezard Card]]
            !{{LA|Bite Bug (Final Fantasy VIII card)|Bite Bug}}
            |[[File:TTBiteBug.png|120px|Bite Bug Card]]
            """;

        var cards = WikiClient.ParseCardList(listPage);

        Assert.Equal(2, cards.Count);
        Assert.Equal("Geezard (Final Fantasy VIII card)", cards[0].Title);
        Assert.Equal("Geezard", cards[0].Name);
        Assert.Equal("Bite Bug", cards[1].Name);
    }
}

public class InfoboxFieldTests
{
    /// <summary>
    /// Reproduces a defect seen in live data: Clive Rosfield's role was stored as
    /// "First Shield of RosariaMarquess of RosariaDominant of Ifrit" because the &lt;br&gt;
    /// separating each title was stripped like any other tag.
    /// </summary>
    [Fact]
    public void SeparatesMultipleValuesSplitByLineBreaks()
    {
        const string infobox = """
            {{infobox character
            |occupation=First Shield of Rosaria<br>Marquess of Rosaria<br />Dominant of Ifrit
            }}
            """;

        Assert.Equal(
            "First Shield of Rosaria, Marquess of Rosaria, Dominant of Ifrit",
            WikiClient.ParseInfoboxField(infobox, "occupation"));
    }

    [Fact]
    public void CollapsesSeparatorsLeftByRemovedSegments()
    {
        const string infobox = """
            {{infobox character
            |affiliation=<br>Shinra<br><br>SOLDIER<br>
            }}
            """;

        Assert.Equal("Shinra, SOLDIER", WikiClient.ParseInfoboxField(infobox, "affiliation"));
    }

    [Fact]
    public void DropsImageEmbedsRatherThanUnwrappingThem()
    {
        const string infobox = """
            {{infobox character
            |race=[[File:Icon.png|15px]] Hyur
            }}
            """;

        Assert.Equal("Hyur", WikiClient.ParseInfoboxField(infobox, "race"));
    }

    /// <summary>
    /// Character infoboxes name an ability field per release, so Cloud's commands sit under
    /// "ffviir abilities" rather than "abilities" — a prefix the enemy stat parser rejects.
    /// </summary>
    [Fact]
    public void ReadsCharacterAbilitiesAcrossReleasePrefixedFields()
    {
        const string cloud = """
            {{infobox character
            |name=Cloud Strife
            |ffviir abilities=Operator Mode/Punisher Mode
            |ffviir2 abilities=Operator Mode/Punisher Mode
            }}
            """;

        Assert.Equal("Operator Mode/Punisher Mode",
            WikiClient.ParseCharacterFieldList(cloud, "abilities", "ability"));
    }

    [Theory]
    [InlineData("|abilities=Trance/Revert", "Trance/Revert")]
    [InlineData("|abilities=Blk Mag, Focus", "Blk Mag, Focus")]
    [InlineData("|abilities=[[Blue Magic]], [[Steal]]", "Blue Magic, Steal")]
    public void ParsesCharacterAbilityLists(string line, string expected)
    {
        Assert.Equal(expected,
            WikiClient.ParseCharacterFieldList("{{infobox character\n" + line + "\n}}", "abilities", "ability"));
    }

    [Fact]
    public void UnwrapsWikilinksToTheirDisplayText()
    {
        const string infobox = """
            {{infobox character
            |home=[[Midgar|the slums of Midgar]]
            }}
            """;

        Assert.Equal("the slums of Midgar", WikiClient.ParseInfoboxField(infobox, "home"));
    }
}

/// <summary>
/// Each game's enemy template names its stat fields differently, so the fixtures here are
/// verbatim excerpts from one enemy article per template generation.
/// </summary>
public class MonsterParsingTests
{
    // Final Fantasy VI — plain field names, affinities stated as words.
    private const string BombVI = """
        {{infobox enemy
        | name = Bomb
        | image = <gallery>
        BombFF6.PNG|SNES/PS/GBA/PR
        Bomb-ffvi-ios.png|2014
        </gallery>
        |location = [[Phantom Train (Final Fantasy VI)|Phantom Train]]; [[Bomb forest]]
        }}
        == Stats ==
        {{infobox enemy stats FFVI
        | level = 8
        | hp = 160
        | mp = 50
        | speed = 30
        | attack = 10
        | defense = 90
        | magic = 1
        | magic defense = 150
        | exp = 35
        | gil = 80
        | ice = Weak
        | water = Weak
        | fire = Absorb
        | snes steal 1 = [[Potion (Final Fantasy VI)|Tonic]]
        | gba steal 2 = [[Hi-Potion (Final Fantasy VI)|Hi-Potion]]
        | snes drop 1 = [[Hi-Potion (Final Fantasy VI)|Potion]]
        | snes special attack = [[Final Fantasy VI enemy abilities#Hit|Hit]]
        | snes other abilities = [[Final Fantasy VI enemy abilities#Blaze|Blaze]], [[Final Fantasy VI enemy abilities#Self-Destruct|Exploder]]
        | blind = Immune
        | poison status = Immune
        }}
        """;

    // Final Fantasy IV — every field is prefixed with the version block it belongs to,
    // and the article repeats the whole block for the Easy Type release.
    private const string BombIV = """
        {{infobox enemy stats FFIV
        | 1 level = 14
        | 1 hp = 55
        | 1 mp = 3
        | 1 gil = 76
        | 1 exp = 361
        | 1 poison = Immune
        | sec 2 = Easy Type
        | 2 level = 14
        | 2 hp = 50
        }}
        """;

    // Final Fantasy XII — level bands put the stats in "min"/"max" pairs.
    private const string BombXII = """
        {{infobox enemy stats FFXII
        | 1 level min = 6
        | 1 hp min = 317
        | 1 mp min = 300
        | 1 exp min = 154
        | 2 hp min = 5,090
        | gil = 0
        | 1 fire = Absorb
        | 1 water = Weak
        | sleep = Immune
        }}
        """;

    // Final Fantasy XV — affinities as damage multipliers, "Absorbs" rather than "Absorb",
    // and weapon weaknesses sharing the same syntax as the elemental ones.
    private const string BombXV = """
        {{infobox enemy stats FFXV
        | level = 15
        | hp = 5,600
        | exp = 17
        | swords = Weak
        | daggers = Weak
        | fire = Absorbs
        | ice = 300%
        | light = Weak
        }}
        """;

    [Fact]
    public void ReadsBattleStatsFromPlainFieldNames()
    {
        var stats = WikiClient.ParseMonsterStats(BombVI);

        Assert.Equal(160, stats.HitPoints);
        Assert.Equal(50, stats.MagicPoints);
        Assert.Equal(8, stats.Level);
        Assert.Equal(35, stats.Experience);
        Assert.Equal(80, stats.Gil);
    }

    [Fact]
    public void ReadsTheFirstBlockOfVersionPrefixedStats()
    {
        var stats = WikiClient.ParseMonsterStats(BombIV);

        Assert.Equal(55, stats.HitPoints);   // not the Easy Type block's 50
        Assert.Equal(14, stats.Level);
        Assert.Equal(361, stats.Experience);
        Assert.Equal(76, stats.Gil);
    }

    [Fact]
    public void FallsBackToMinimumValuesForLevelBandedStats()
    {
        var stats = WikiClient.ParseMonsterStats(BombXII);

        Assert.Equal(317, stats.HitPoints);
        Assert.Equal(300, stats.MagicPoints);
        Assert.Equal(6, stats.Level);
        Assert.Equal(154, stats.Experience);
    }

    [Fact]
    public void ParsesThousandsSeparators()
    {
        Assert.Equal(5_600, WikiClient.ParseMonsterStats(BombXV).HitPoints);
    }

    /// <summary>
    /// "| 1 bribe gil = 17,000" is what an FFX enemy costs to bribe, not what it drops.
    /// Only a version number or a platform may sit in front of a stat's field name.
    /// </summary>
    [Fact]
    public void DoesNotMistakeAPrefixedFieldForTheStatItself()
    {
        const string bribeOnly = """
            {{infobox enemy stats FFX
            | 1 bribe gil = 17,000
            | 1 max hp = 850
            }}
            """;

        Assert.Null(WikiClient.ParseMonsterStats(bribeOnly).Gil);
        Assert.Equal(850, WikiClient.ParseMonsterStats(bribeOnly).HitPoints);
    }

    [Fact]
    public void SortsElementalAffinitiesIntoWeaknessesAndAbsorptions()
    {
        var stats = WikiClient.ParseMonsterStats(BombVI);

        Assert.Equal("Ice, Water", stats.Weaknesses);
        Assert.Equal("Fire", stats.Absorbs);
    }

    [Fact]
    public void ReadsAffinitiesStatedAsDamageMultipliers()
    {
        var stats = WikiClient.ParseMonsterStats(BombXV);

        // Ice at 300% is a weakness; the sword and dagger fields are not elements at all.
        Assert.Equal("Ice, Holy", stats.Weaknesses);
        Assert.Equal("Fire", stats.Absorbs);
    }

    /// <summary>
    /// Final Fantasy VIII scales enemy stats off the party's level, so its infobox holds
    /// formula coefficients instead of HP, and its affinities are bare percentages where 100
    /// is neutral and a negative value heals.
    /// </summary>
    [Fact]
    public void ReadsAffinitiesStatedAsBarePercentages()
    {
        const string cactuarVIII = """
            {{infobox enemy stats FFVIII
            | hp a = 0.1
            | hp b = 2
            | water = 290
            | fire = -100
            | thunder = 100
            | poison = 20
            }}
            """;

        var stats = WikiClient.ParseMonsterStats(cactuarVIII);

        Assert.Null(stats.HitPoints);              // "hp a" is a coefficient, not a stat
        Assert.Equal("Water", stats.Weaknesses);   // thunder at 100 is neutral
        Assert.Equal("Fire", stats.Absorbs);
    }

    [Fact]
    public void IgnoresStatusAilmentFieldsThatShareTheAffinitySyntax()
    {
        const string statusesOnly = """
            {{infobox enemy stats FFX
            | 1 silence = 20
            | 1 darkness = 20
            | 1 sleep = Immune
            | poison% = 25
            }}
            """;

        var stats = WikiClient.ParseMonsterStats(statusesOnly);

        Assert.Null(stats.Weaknesses);
        Assert.Null(stats.Absorbs);
    }

    [Fact]
    public void ReadsCombatStatsUnderEachGameSpelling()
    {
        // FFVI says "speed"/"magic defense"; FFVII says "dexterity"/"magic def"/"magic atk".
        Assert.Equal(10, WikiClient.ParseMonsterStats(BombVI).Attack);
        Assert.Equal(90, WikiClient.ParseMonsterStats(BombVI).Defense);
        Assert.Equal(150, WikiClient.ParseMonsterStats(BombVI).MagicDefense);

        const string bombVII = """
            {{infobox enemy stats FFVII
            | attack = 24
            | magic atk = 22
            | defense = 30
            | magic def = 30
            | dexterity = 65
            }}
            """;

        var stats = WikiClient.ParseMonsterStats(bombVII);
        Assert.Equal(24, stats.Attack);
        Assert.Equal(22, stats.MagicAttack);
        Assert.Equal(30, stats.Defense);
        Assert.Equal(30, stats.MagicDefense);
        Assert.Equal(65, stats.Speed);
    }

    [Fact]
    public void CollectsAbilitiesAcrossThePlatformVariantsOfTheField()
    {
        // FFVI splits an enemy's moves over several platform-prefixed fields.
        var abilities = WikiClient.ParseMonsterStats(BombVI).Abilities;

        Assert.NotNull(abilities);
        Assert.Contains("Blaze", abilities);
        Assert.Contains("Exploder", abilities);
    }

    /// <summary>
    /// FFVII lists the same move once per AI slot, so the raw field reads
    /// "Bodyblow, Bodyblow, Bodyblow, Fireball, Bomb Blast".
    /// </summary>
    [Fact]
    public void DeduplicatesRepeatedAbilities()
    {
        const string wikitext = """
            {{infobox enemy stats FFVII
            | abilities = ''Bodyblow'', ''Bodyblow'', ''Bodyblow'', [[Fireball]], [[Bomb Blast]]
            }}
            """;

        Assert.Equal("Bodyblow, Fireball, Bomb Blast", WikiClient.ParseMonsterStats(wikitext).Abilities);
    }

    /// <summary>
    /// In FFX "weapon abilities" and "armor abilities" are what the player can customize onto
    /// gear using this enemy's drops — not moves the enemy has. Reading them as the enemy's
    /// abilities would credit the Bomb with Firestrike and Fire Ward.
    /// </summary>
    [Fact]
    public void DoesNotMistakePlayerCustomizationsForEnemyAbilities()
    {
        const string bombX = """
            {{infobox enemy stats FFX
            | weapon abilities = [[Piercing]], [[Firestrike]], [[Distill Power]]
            | armor abilities = [[Fire Ward]]
            | abilities = ''Rush'', [[Fire]], [[Self Destruct]]
            }}
            """;

        var abilities = WikiClient.ParseMonsterStats(bombX).Abilities;

        Assert.Equal("Rush, Fire, Self Destruct", abilities);
        Assert.DoesNotContain("Firestrike", abilities);
        Assert.DoesNotContain("Fire Ward", abilities);
    }

    [Fact]
    public void ReadsDropsAndStealsSeparately()
    {
        var stats = WikiClient.ParseMonsterStats(BombVI);

        Assert.Equal("Potion", stats.Drops);
        Assert.Equal("Tonic, Hi-Potion", stats.Steals);
    }

    // Final Fantasy V's enemy infobox was rewritten in 2026 to hyphenate its field names and
    // to tag per-platform values with a hyphen too ("ps-abilities"). Nothing in the template
    // changed meaning — but every FFV enemy read as having no magic defence, no drops and
    // nothing to steal until the field matcher accepted the hyphen.
    private const string GoblinV = """
        {{infobox enemy
        | name = Goblin
        | release = FFV
        }}
        ==Stats==
        {{Infobox enemy info FFV
        |lv=6
        |hp=16
        |strength=5
        |defense=0
        |magic=0
        |magic-defense=5
        |magic-evasion=0
        |agility=10
        |attack-multiplier=1
        |gil=20
        |exp=10
        |steal-1=[[Potion (Final Fantasy V)|Potion]]
        |steal-2=Nothing
        |drop-1=Nothing
        |drop-2=[[Leather Shoes (Final Fantasy V)|Leather Shoes]]
        |abilities=!Attack
        |ps-abilities=!Fight
        }}
        """;

    [Fact]
    public void ReadsHyphenatedFieldNames()
    {
        var stats = WikiClient.ParseMonsterStats(GoblinV);

        Assert.Equal(5, stats.MagicDefense);
        Assert.Equal("Leather Shoes", stats.Drops);
        Assert.Equal("Potion", stats.Steals);
    }

    [Fact]
    public void ReadsHyphenatedPlatformTags()
    {
        Assert.Equal("!Attack, !Fight", WikiClient.ParseMonsterStats(GoblinV).Abilities);
    }

    /// <summary>
    /// The level-banded games state defence and attack the same way they state HP, so the
    /// "min" form is read for those too.
    /// </summary>
    [Fact]
    public void FallsBackToMinimumValuesForBandedCombatStats()
    {
        var stats = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFXII
            | release = FFXII
            | attack power min = 63
            | attack power max = 69
            | defense min = 17
            | defense max = 21
            | magick power min = 21
            | magick resist min = 30
            }}
            """);

        Assert.Equal(63, stats.Attack);
        Assert.Equal(17, stats.Defense);
        Assert.Equal(21, stats.MagicAttack);
        Assert.Equal(30, stats.MagicDefense);
    }

    /// <summary>
    /// Vitality and spirit are only the defence stats in Final Fantasy XV, whose enemy template
    /// carries nothing else. Final Fantasy XII lists both as stats of their own next to a real
    /// defence, and reading them there reports 46 where the article says 17.
    /// </summary>
    [Fact]
    public void ReadsVitalityAndSpiritAsDefencesOnlyForFinalFantasyXV()
    {
        var xv = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFXV
            | release = FFXV
            | 1 strength = 700
            | 1 vitality = 66
            | 1 spirit = 51
            }}
            """);

        Assert.Equal(66, xv.Defense);
        Assert.Equal(51, xv.MagicDefense);

        var xii = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFXII
            | release = FFXII
            | defense min = 17
            | magick resist min = 30
            | vitality = 46
            }}
            """);

        Assert.Equal(17, xii.Defense);
        Assert.Equal(30, xii.MagicDefense);
    }

    [Fact]
    public void ReadsFinalFantasyXIIsNumberedMoveFields()
    {
        var stats = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFXII
            | release = FFXII
            | magickname1 = Aero
            | magickcond1 = HP <50%
            | magickname2 = Cura
            | technickname1 = Souleater
            }}
            """);

        Assert.Equal("Aero, Cura, Souleater", stats.Abilities);
    }

    /// <summary>
    /// Final Fantasy XV's "element drop" is the elemental deposit an enemy yields for magic
    /// crafting, not an item it leaves behind.
    /// </summary>
    [Fact]
    public void DoesNotReadAnElementalDepositAsAnItemDrop()
    {
        var stats = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFXV
            | release = FFXV
            | 1 primary drop = [[Garula Sirloin]]
            | 1 element drop = Ice
            | 1 element drop quantity = 4
            }}
            """);

        Assert.Equal("Garula Sirloin", stats.Drops);
    }

    /// <summary>
    /// A field assignment that leaks onto the value's line is stripped even when its name
    /// starts with a platform tag rather than a letter.
    /// </summary>
    [Fact]
    public void StripsLeakedFieldAssignmentsNamedWithADigit()
    {
        var stats = WikiClient.ParseMonsterStats("""
            {{infobox enemy stats FFIII
            | release = FFIII
            | 3d steal = | 3d common drop = | 3d uncommon drop =
            | nes steal = [[Potion (item)|Potion]]
            }}
            """);

        Assert.Equal("Potion", stats.Steals);
        Assert.Null(stats.Drops);
    }

    /// <summary>
    /// Articles are hand-written and occasionally malformed. A wikilink with a doubled pipe
    /// still reduces to its display text rather than leaving markup in the stored value.
    /// </summary>
    [Theory]
    [InlineData("[[Hi-Ether (Final Fantasy VI)||Hi-Ether]]", "Hi-Ether")]
    [InlineData("[[Hi-Ether (Final Fantasy VI)|Hi-Ether]]", "Hi-Ether")]
    [InlineData("[[Hi-Ether]]", "Hi-Ether")]
    public void ReducesAMalformedWikilinkToItsDisplayText(string link, string expected)
    {
        var stats = WikiClient.ParseMonsterStats(
            "{{infobox enemy stats FFVI\n| gba steal 2 = " + link + "\n}}");

        Assert.Equal(expected, stats.Steals);
    }

    [Theory]
    [InlineData("| abilities = None")]
    [InlineData("| abilities = N/A")]
    [InlineData("| abilities = —")]
    [InlineData("| abilities = true")]
    public void IgnoresPlaceholderValues(string line)
    {
        Assert.Null(WikiClient.ParseMonsterStats("{{infobox enemy stats\n" + line + "\n}}").Abilities);
    }

    [Fact]
    public void ReturnsNoStatsForAnArticleWithoutAStatsInfobox()
    {
        var stats = WikiClient.ParseMonsterStats("The '''Bomb''' is an enemy in ''Final Fantasy VI''.");

        Assert.Equal(MonsterStats.Empty, stats);
    }

    [Fact]
    public void TakesTheFirstGalleryEntryAsTheImageFile()
    {
        Assert.Equal("BombFF6.PNG", WikiClient.ParseImageFileName(BombVI));
    }

    [Theory]
    [InlineData("| image = XII bomb render.png", "XII bomb render.png")]
    [InlineData("| image = [[File:Bomb FFXV.png|200px]]", "Bomb FFXV.png")]
    [InlineData("| image = Bomb from FFX.png", "Bomb from FFX.png")]
    public void ReadsTheImageFileNameFromTheInfobox(string line, string expected)
    {
        Assert.Equal(expected, WikiClient.ParseImageFileName("{{infobox enemy\n" + line + "\n}}"));
    }

    [Theory]
    [InlineData("{{infobox enemy\n| name = Bomb\n}}")]
    [InlineData("{{infobox enemy\n| image = \n}}")]
    public void ReturnsNoImageFileNameWhenTheFieldIsMissingOrEmpty(string wikitext)
    {
        Assert.Null(WikiClient.ParseImageFileName(wikitext));
    }
}

/// <summary>
/// Found in live data: the enemy categories carry a game's collective reference pages
/// alongside its actual monsters, and those pages are long and heavily linked enough to
/// score a perfect 100 — they ranked above every real boss in the scraped table.
/// </summary>
public class MetaArticleTests
{
    [Theory]
    [InlineData("Final Fantasy VII enemy abilities")]
    [InlineData("Final Fantasy XIV enemy actions")]
    [InlineData("Final Fantasy VI enemy formations")]
    [InlineData("Final Fantasy X enemy stats")]
    [InlineData("Final Fantasy XIV enemies")]
    [InlineData("Final Fantasy VI Bestiary")]
    [InlineData("List of Final Fantasy XII enemies")]
    public void ExcludesCollectiveReferencePages(string title)
    {
        Assert.True(WikiScoring.IsMetaArticle(title));
    }

    [Theory]
    [InlineData("Gilgamesh (Final Fantasy V)")]
    [InlineData("Neo Exdeath")]
    [InlineData("Bomb (Final Fantasy VI)")]
    [InlineData("Dodore")]
    [InlineData("Warmech")]
    [InlineData("Ahriman (Final Fantasy VI)")]
    public void KeepsRealMonsters(string title)
    {
        Assert.False(WikiScoring.IsMetaArticle(title));
    }
}

public class IntroTextTests
{
    /// <summary>
    /// Reproduces a defect seen in live data: the Ruby Dragon article opened with an inline
    /// template whose trailing period survived the strip, so the stored description read
    /// ". Ruby Dragon , also known as Claret Dragon, is a recurring enemy…".
    /// </summary>
    [Fact]
    public void DropsPunctuationLeftBehindByStrippedTemplates()
    {
        const string article = """
            {{infobox enemy|name=Ruby Dragon}}
            {{sic}}. '''Ruby Dragon''' {{J|ルビードラゴン}}, also known as '''Claret Dragon''', is a recurring enemy.
            """;

        Assert.Equal(
            "Ruby Dragon, also known as Claret Dragon, is a recurring enemy.",
            WikiClient.ParseIntroText(article));
    }

    [Fact]
    public void ReturnsNullForRedirectPages()
    {
        Assert.Null(WikiClient.ParseIntroText("#REDIRECT [[Bomb (creature)]]"));
    }
}

public class PopularityScoringTests
{
    // Observed live values: Cloud Strife 118,566 bytes / 500+ backlinks;
    // the FFXIV walk-on NPC "Bi Bi" 41 bytes / 0 backlinks.
    [Fact]
    public void SeriesLeadScoresFarAboveWalkOnNpc()
    {
        var cloud = WikiScoring.ScorePopularity(new PageSignals(118_566, 500));
        var npc = WikiScoring.ScorePopularity(new PageSignals(41, 0));

        Assert.True(cloud > 90, $"expected a lead to score above 90, got {cloud}");
        Assert.True(npc < 10, $"expected a walk-on to score below 10, got {npc}");
    }

    [Fact]
    public void ScoreStaysWithinBounds()
    {
        Assert.InRange(WikiScoring.ScorePopularity(new PageSignals(0, 0)), 0, 100);
        Assert.InRange(WikiScoring.ScorePopularity(new PageSignals(int.MaxValue, 100_000)), 0, 100);
    }

    [Fact]
    public void MissingSignalsScoreZero()
    {
        Assert.Equal(0, WikiScoring.ScorePopularity(null));
    }

    [Fact]
    public void ScoreIncreasesMonotonicallyWithBothSignals()
    {
        var small = WikiScoring.ScorePopularity(new PageSignals(1_000, 5));
        var medium = WikiScoring.ScorePopularity(new PageSignals(10_000, 50));
        var large = WikiScoring.ScorePopularity(new PageSignals(100_000, 400));

        Assert.True(small < medium && medium < large, $"{small} < {medium} < {large}");
    }
}

public class DataRepairTests
{
    [Theory]
    [InlineData("Noctis  XV party member", "Noctis")]
    [InlineData("Agrias Oaks  XIV", "Agrias Oaks")]
    [InlineData("2B  XIV", "2B")]
    [InlineData("Adventurer  XI", "Adventurer")]
    [InlineData("Butch  VII", "Butch")]
    public void StripsLegacyGameNumeralSuffix(string damaged, string expected)
    {
        Assert.Equal(expected, WikiText.RepairName(damaged));
    }

    [Theory]
    [InlineData("Cloud Strife")]
    [InlineData("Tifa Lockhart")]
    [InlineData("Vivi Ornitier")]
    public void LeavesCleanNamesUntouched(string name)
    {
        Assert.Equal(name, WikiText.RepairName(name));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = WikiText.RepairName("Noctis  XV party member");
        var twice = WikiText.RepairName(once);

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("|race=Android")]
    [InlineData("|occupation=")]
    [InlineData("|education=")]
    [InlineData("Ala Mhigo|abilities=")]
    // Field names containing spaces — these survived an earlier scrape and reached the database.
    [InlineData("|japanese voice actor = Mizue Tsunashima")]
    [InlineData("Scions of the Seventh Dawn|japanese voice actor =")]
    [InlineData("{{unparsed}}")]
    [InlineData("[[Still a link]]")]
    [InlineData("   ")]
    public void NullsOutUnparsedInfoboxFragments(string junk)
    {
        Assert.Null(WikiText.Clean(junk));
    }

    [Theory]
    [InlineData("SOLDIER", "SOLDIER")]
    [InlineData("  Avalanche  ", "Avalanche")]
    [InlineData("Garlean Empire", "Garlean Empire")]
    public void KeepsCleanValues(string value, string expected)
    {
        Assert.Equal(expected, WikiText.Clean(value));
    }
}

/// <summary>
/// The monster table carried the same damage the character table did, plus two variants:
/// a stranded "boss" disambiguator and a parenthetical whose closing half went missing with
/// the stripped game title. All fixtures are real names read out of the live database.
/// </summary>
public class MonsterRepairTests
{
    [Theory]
    [InlineData("Lamia  IV", "Lamia")]
    [InlineData("Abyss Worm  II", "Abyss Worm")]
    [InlineData("Adamantoise  II", "Adamantoise")]
    [InlineData("Chaos  boss", "Chaos")]
    [InlineData("Garland  boss", "Garland")]
    [InlineData("Borghen (boss", "Borghen")]
    [InlineData("Emperor (final boss", "Emperor")]
    public void RestoresTheNameTheWikiActuallyUses(string damaged, string expected)
    {
        Assert.Equal(expected, WikiText.RepairMonsterName(damaged));
    }

    [Theory]
    [InlineData("Gilgamesh")]
    [InlineData("Neo Exdeath")]
    [InlineData("Ruby Weapon")]
    [InlineData("Magic Pot")]
    public void LeavesCleanNamesUntouched(string name)
    {
        Assert.Equal(name, WikiText.RepairMonsterName(name));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = WikiText.RepairMonsterName("Lamia  IV");
        var twice = WikiText.RepairMonsterName(once);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// An intact wiki title carries its disambiguator in brackets, and the catalogue stores
    /// none of them: the game is a column, so "Bomb (Final Fantasy II)" is filed as "Bomb".
    /// Importing a page has to make the same reduction or every import arrives as a stranger
    /// to the row it duplicates.
    /// </summary>
    [Theory]
    [InlineData("Bomb (Final Fantasy II)", "Bomb")]
    [InlineData("Auron (Final Fantasy X party member)", "Auron")]
    [InlineData("Cid (Final Fantasy VII)", "Cid")]
    [InlineData("Gilgamesh", "Gilgamesh")]
    [InlineData("Magic Pot", "Magic Pot")]
    public void DropsTheWikiDisambiguator(string title, string expected)
    {
        Assert.Equal(expected, WikiText.NormalizeName(title));
    }

    [Fact]
    public void NormalizingIsIdempotent()
    {
        var once = WikiText.NormalizeName("Bomb (Final Fantasy II)");

        Assert.Equal(once, WikiText.NormalizeName(once));
    }

    [Theory]
    [InlineData("Flare  enemy ability")]
    [InlineData("Blizzaga  enemy ability")]
    [InlineData("Healaga (enemy ability")]
    [InlineData("Dragon  enemy type")]
    [InlineData("Final Fantasy II enemies")]
    [InlineData("Final Fantasy VI Bestiary")]
    // Survivors of the first purge: the plural form, and a row named simply "Enemy".
    [InlineData("Final Fantasy V enemy types")]
    [InlineData("Final Fantasy IX enemy types")]
    [InlineData("Enemy")]
    [InlineData("enemies")]
    public void IdentifiesRowsThatWereNeverMonsters(string name)
    {
        Assert.True(WikiText.IsNotAMonster(name));
    }

    /// <summary>A monster whose name merely starts with "enemy" wording must survive.</summary>
    [Theory]
    [InlineData("Enemy Launcher")]
    [InlineData("Enemy Skill Materia Keeper")]
    public void KeepsMonstersWhoseNamesBeginWithThatWording(string name)
    {
        Assert.False(WikiText.IsNotAMonster(name));
    }

    [Theory]
    [InlineData("Lamia  IV")]
    [InlineData("Chaos  boss")]
    [InlineData("Gilgamesh")]
    [InlineData("Dodore")]
    [InlineData("Bomb")]
    public void DoesNotMistakeADamagedMonsterForAReferencePage(string name)
    {
        Assert.False(WikiText.IsNotAMonster(name));
    }
}

/// <summary>
/// Fixtures are verbatim excerpts of the live character navbox templates, so these fail if the
/// wiki restructures the group/list layout the roster is read from.
/// </summary>
public class PlayableRosterParsingTests
{
    // Template:Navbox characters FFVII — the plain shape: a "Playable" group with its names on
    // the matching list line.
    private const string SeventhNavbox = """
        | 1 group = Playable
        | 1 list = [[Cloud Strife]] - [[Barret Wallace]] - [[Tifa Lockhart]] - [[Aerith Gainsborough|Aerith Gainsborough]] - [[Red XIII]] - [[Cait Sith (Final Fantasy VII)|Cait Sith]] - [[Cid Highwind]] - [[Yuffie Kisaragi]] - [[Vincent Valentine]]
        | 2 group = Temporary playable
        | 2 list = [[Sephiroth]]
        | 3 group = [[Shinra Electric Power Company|Shinra Staff]]
        | 3 list = [[Rufus Shinra]] - [[President Shinra]] - [[Professor Hojo]] - [[Tseng]] - [[Reno]]
        | 4 group = Other
        | 4 list = [[Biggs (Final Fantasy VII)|Biggs]] - [[Butch (Final Fantasy VII)|Butch]] - [[Jenova]]
        """;

    // Template:Navbox characters FFXII — the nested shape: the "Playable" group holds nothing
    // itself and hangs its names off subgroups.
    private const string TwelfthNavbox = """
        | 1 group =  Playable
        | 1.1 group = Main
        | 1.1 list = [[Vaan]] - [[Penelo]] - [[Balthier]] - [[Fran (Final Fantasy XII)|Fran]] - [[Basch fon Ronsenburg]] - [[Ashelia B'nargin Dalmasca]]
        | 1.2 group = Temporary
        | 1.2 list = [[Reks]]
        | 1.3 group = Guests
        | 1.3 list = [[Larsa Ferrinas Solidor]] - [[Vossler York Azelas]] - [[Reddas]]
        | 1.4 group = AI members
        | 1.4 list = [[Bangaa Hunter]] - [[Krjn]] - [[Monid]]
        | 3 group = Non-playable
        | 3 list = [[Al-Cid Margrace]] - [[Anastasis]] - [[Old Dalan|Dalan]]
        """;

    // Template:Navbox characters FFXV and FFIII — the same idea under labels that never say
    // "Playable" in the plain form.
    private const string FifteenthNavbox = """
        | 1 group = Main party
        | 1 list = [[Noctis Lucis Caelum]] - [[Gladiolus Amicitia]] - [[Ignis Scientia]] - [[Prompto Argentum]]
        | 2 group = Guests
        | 2 list = [[Aranea Highwind]] - [[Cor Leonis]] - [[Iris Amicitia]]
        | 3 group = Antagonists
        | 3 list = [[Ardyn Izunia]] - [[Iedolas Aldercapt]]
        """;

    private const string ThirdNavbox = """
        | 1 group = Famicon playable
        | 1 list = [[Onion Knight (Final Fantasy III)|Onion Knights]]
        | 2 group = Remake playable
        | 2 list = [[Luneth]] - [[Arc]] - [[Refia]] - [[Ingus]]
        | 3 group = Guests
        | 3 list = [[Desch]] - [[Cid Haze]]
        """;

    [Fact]
    public void ReadsThePlayableGroup()
    {
        var roster = WikiClient.ParsePlayableRoster(SeventhNavbox);

        Assert.Contains("Cloud Strife", roster);
        Assert.Contains("Tifa Lockhart", roster);
        Assert.Contains("Vincent Valentine", roster);
    }

    /// <summary>
    /// The display text wins over the article title, because that is the name the character
    /// scraper stored: "[[Cait Sith (Final Fantasy VII)|Cait Sith]]" has to match the row.
    /// </summary>
    [Fact]
    public void PrefersTheDisplayNameOverTheArticleTitle()
    {
        var roster = WikiClient.ParsePlayableRoster(SeventhNavbox);

        Assert.Contains("Cait Sith", roster);
        Assert.DoesNotContain("Cait Sith (Final Fantasy VII)", roster);
    }

    /// <summary>
    /// Sephiroth is playable for exactly one flashback, and the wiki says so. Including the
    /// temporary group is what puts him on the roster.
    /// </summary>
    [Fact]
    public void IncludesTemporarilyPlayableCharacters() =>
        Assert.Contains("Sephiroth", WikiClient.ParsePlayableRoster(SeventhNavbox));

    [Fact]
    public void LeavesOutEveryoneWhoIsNotPlayable()
    {
        var roster = WikiClient.ParsePlayableRoster(SeventhNavbox);

        Assert.DoesNotContain("Rufus Shinra", roster);
        Assert.DoesNotContain("Professor Hojo", roster);
        Assert.DoesNotContain("Butch", roster);
        Assert.DoesNotContain("Jenova", roster);
    }

    /// <summary>
    /// Final Fantasy X and XII put nothing in the "Playable" group itself and hang the names off
    /// nested subgroups, so a reader that only looks at top-level groups returns nothing for
    /// either game.
    /// </summary>
    [Fact]
    public void ReadsNamesNestedUnderAPlayableGroup()
    {
        var roster = WikiClient.ParsePlayableRoster(TwelfthNavbox);

        Assert.Contains("Vaan", roster);
        Assert.Contains("Ashelia B'nargin Dalmasca", roster);
        Assert.Contains("Reks", roster);
    }

    /// <summary>
    /// Guests and AI members are escorts the player never controls — a Bangaa Hunter is not a
    /// character anyone picked.
    /// </summary>
    [Fact]
    public void LeavesOutGuestsAndAiMembers()
    {
        var roster = WikiClient.ParsePlayableRoster(TwelfthNavbox);

        Assert.DoesNotContain("Reddas", roster);
        Assert.DoesNotContain("Bangaa Hunter", roster);
        Assert.DoesNotContain("Krjn", roster);
    }

    /// <summary>"Non-playable" contains the word and must never match on it.</summary>
    [Fact]
    public void IsNotFooledByTheNonPlayableGroup()
    {
        var roster = WikiClient.ParsePlayableRoster(TwelfthNavbox);

        Assert.DoesNotContain("Al-Cid Margrace", roster);
        Assert.DoesNotContain("Dalan", roster);
    }

    [Fact]
    public void ReadsAPartyThatIsNotLabelledPlayable()
    {
        var roster = WikiClient.ParsePlayableRoster(FifteenthNavbox);

        Assert.Equal(["Noctis Lucis Caelum", "Gladiolus Amicitia", "Ignis Scientia", "Prompto Argentum"], roster);
    }

    [Fact]
    public void ReadsBothOfTheThirdGamesPlayableGroups()
    {
        var roster = WikiClient.ParsePlayableRoster(ThirdNavbox);

        Assert.Contains("Luneth", roster);
        Assert.Contains("Ingus", roster);
        Assert.Contains("Onion Knights", roster);
        Assert.DoesNotContain("Desch", roster);
    }

    /// <summary>
    /// Final Fantasy's navbox groups its party as "Warriors of Light" and lists the six job
    /// classes rather than characters, so it legitimately yields nothing and the scraper has to
    /// treat an empty roster as a game to skip rather than an error.
    /// </summary>
    [Fact]
    public void ReturnsNothingWhenAGameListsNoPlayableCharacters()
    {
        const string first = """
            | 1 group = [[Warriors of Light]]
            | 1 list = [[Warrior (Final Fantasy)|Warrior]] - [[Thief (Final Fantasy)|Thief]] - [[Monk (Final Fantasy)|Monk]]
            | 2 group = Fiends of Chaos
            | 2 list = [[Lich (Final Fantasy)|Lich]] - [[Marilith (Final Fantasy)|Marilith]]
            """;

        Assert.Empty(WikiClient.ParsePlayableRoster(first));
    }

    [Fact]
    public void DoesNotListACharacterTwice()
    {
        const string repeated = """
            | 1 group = Playable
            | 1 list = [[Vaan]] - [[Penelo]]
            | 2 group = Temporary playable
            | 2 list = [[Vaan]]
            """;

        Assert.Equal(["Vaan", "Penelo"], WikiClient.ParsePlayableRoster(repeated));
    }
}

/// <summary>
/// Fixtures are verbatim excerpts of live character articles, so these fail if the wiki changes
/// how it names the per-release infobox fields.
/// </summary>
public class CharacterFieldParsingTests
{
    // Tifa Lockhart — Final Fantasy VII prefixes every field with the release it belongs to,
    // and carries a second, qualified weapon field alongside the real one.
    private const string PrefixedInfobox = """
        |name=Tifa Lockhart
        |occupation=Bar hostess, [[Avalanche (group)|Avalanche]] member
        |ffvii weapon=[[Final Fantasy VII weapons#Tifa's knuckles|Knuckles]]
        |ffvii ultimate weapon=[[Premium Heart (Final Fantasy VII)|Premium Heart]]
        |ffviir weapon=[[Final Fantasy VII Remake weapons#Tifa's knuckles|Knuckles]]
        """;

    // Vivi Ornitier — Final Fantasy IX writes the bare field names.
    private const string BareInfobox = """
        |name=Vivi Ornitier
        |job=[[Black Mage (job)|Black Mage]]
        |weapon=[[Final Fantasy IX weapons#Staves|Staves]]
        |ultimate weapon=Mace of Zeus
        """;

    // Cloud Strife — the qualified field sits between two real ones.
    private const string CloudInfobox = """
        |ffvii weapon=[[Final Fantasy VII weapons#Cloud's broadswords|Broadswords]]
        |ffvii ultimate weapon=[[Ultima Weapon (Final Fantasy VII)|Ultima Weapon]]
        |ffviir2 weapon=[[Final Fantasy VII Rebirth weapons#Cloud's broadswords|Broadswords]]
        """;

    [Fact]
    public void ReadsABareField() =>
        Assert.Equal("Staves", WikiClient.ParseCharacterField(BareInfobox, "weapon", "weapons"));

    [Fact]
    public void ReadsAJob() =>
        Assert.Equal("Black Mage", WikiClient.ParseCharacterField(BareInfobox, "job", "class"));

    /// <summary>
    /// The bug this exists for: reading only the bare form left every Final Fantasy VII
    /// character with no weapon, and so no battle role — half the roster came out Balanced.
    /// </summary>
    [Fact]
    public void ReadsAFieldPrefixedWithItsRelease() =>
        Assert.Equal("Knuckles", WikiClient.ParseCharacterField(PrefixedInfobox, "weapon", "weapons"));

    /// <summary>
    /// "|ultimate weapon=" names one late-game item rather than the class of arms a character
    /// uses. Letting the release prefix swallow the qualifier calls Cloud's weapon
    /// "Ultima Weapon" — a proper noun no archetype pattern matches — instead of "Broadswords".
    /// </summary>
    [Theory]
    [InlineData(nameof(CloudInfobox))]
    public void IgnoresTheUltimateWeaponField(string _)
    {
        Assert.Equal("Broadswords", WikiClient.ParseCharacterField(CloudInfobox, "weapon", "weapons"));
        Assert.Equal("Staves", WikiClient.ParseCharacterField(BareInfobox, "weapon", "weapons"));
    }

    [Fact]
    public void ReturnsNullWhenTheFieldIsAbsent() =>
        Assert.Null(WikiClient.ParseCharacterField("|name=Broom\n|race=Object", "weapon", "weapons"));

    /// <summary>The occupation field is adjacent and must never be mistaken for a weapon.</summary>
    [Fact]
    public void DoesNotConfuseANeighbouringField() =>
        Assert.Null(WikiClient.ParseCharacterField(PrefixedInfobox, "job", "class"));
}
