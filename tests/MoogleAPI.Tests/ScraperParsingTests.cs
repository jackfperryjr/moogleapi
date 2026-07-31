using MoogleAPI.Scraper;
using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// Fixtures are verbatim excerpts of live Final Fantasy Wiki articles, so these tests fail
/// if the wiki's infobox layout drifts away from what the scraper expects.
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
        | magic defense = 150
        | exp = 35
        | gil = 80
        | ice = Weak
        | water = Weak
        | fire = Absorb
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
        Assert.True(MonsterScraper.IsMetaArticle(title));
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
        Assert.False(MonsterScraper.IsMetaArticle(title));
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
        var cloud = CharacterScraper.ScorePopularity(new PageSignals(118_566, 500));
        var npc   = CharacterScraper.ScorePopularity(new PageSignals(41, 0));

        Assert.True(cloud > 90, $"expected a lead to score above 90, got {cloud}");
        Assert.True(npc < 10, $"expected a walk-on to score below 10, got {npc}");
    }

    [Fact]
    public void ScoreStaysWithinBounds()
    {
        Assert.InRange(CharacterScraper.ScorePopularity(new PageSignals(0, 0)), 0, 100);
        Assert.InRange(CharacterScraper.ScorePopularity(new PageSignals(int.MaxValue, 100_000)), 0, 100);
    }

    [Fact]
    public void MissingSignalsScoreZero()
    {
        Assert.Equal(0, CharacterScraper.ScorePopularity(null));
    }

    [Fact]
    public void ScoreIncreasesMonotonicallyWithBothSignals()
    {
        var small  = CharacterScraper.ScorePopularity(new PageSignals(1_000, 5));
        var medium = CharacterScraper.ScorePopularity(new PageSignals(10_000, 50));
        var large  = CharacterScraper.ScorePopularity(new PageSignals(100_000, 400));

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
        Assert.Equal(expected, DataRepair.RepairName(damaged));
    }

    [Theory]
    [InlineData("Cloud Strife")]
    [InlineData("Tifa Lockhart")]
    [InlineData("Vivi Ornitier")]
    public void LeavesCleanNamesUntouched(string name)
    {
        Assert.Equal(name, DataRepair.RepairName(name));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once  = DataRepair.RepairName("Noctis  XV party member");
        var twice = DataRepair.RepairName(once);

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
        Assert.Null(DataRepair.Clean(junk));
    }

    [Theory]
    [InlineData("SOLDIER", "SOLDIER")]
    [InlineData("  Avalanche  ", "Avalanche")]
    [InlineData("Garlean Empire", "Garlean Empire")]
    public void KeepsCleanValues(string value, string expected)
    {
        Assert.Equal(expected, DataRepair.Clean(value));
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
        Assert.Equal(expected, DataRepair.RepairMonsterName(damaged));
    }

    [Theory]
    [InlineData("Gilgamesh")]
    [InlineData("Neo Exdeath")]
    [InlineData("Ruby Weapon")]
    [InlineData("Magic Pot")]
    public void LeavesCleanNamesUntouched(string name)
    {
        Assert.Equal(name, DataRepair.RepairMonsterName(name));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once  = DataRepair.RepairMonsterName("Lamia  IV");
        var twice = DataRepair.RepairMonsterName(once);

        Assert.Equal(once, twice);
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
        Assert.True(DataRepair.IsNotAMonster(name));
    }

    /// <summary>A monster whose name merely starts with "enemy" wording must survive.</summary>
    [Theory]
    [InlineData("Enemy Launcher")]
    [InlineData("Enemy Skill Materia Keeper")]
    public void KeepsMonstersWhoseNamesBeginWithThatWording(string name)
    {
        Assert.False(DataRepair.IsNotAMonster(name));
    }

    [Theory]
    [InlineData("Lamia  IV")]
    [InlineData("Chaos  boss")]
    [InlineData("Gilgamesh")]
    [InlineData("Dodore")]
    [InlineData("Bomb")]
    public void DoesNotMistakeADamagedMonsterForAReferencePage(string name)
    {
        Assert.False(DataRepair.IsNotAMonster(name));
    }
}
