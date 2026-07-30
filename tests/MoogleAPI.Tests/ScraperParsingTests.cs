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
