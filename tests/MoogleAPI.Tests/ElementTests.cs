using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Tests;

/// <summary>
/// Final Fantasy publishes no "type" on an enemy, so a monster's own element is inferred — and an
/// inference that goes wrong hands out affinity bonuses and resistances the article never claimed.
/// These pin the evidence ladder and the grid that fills the gaps around it.
/// </summary>
public class ElementTests
{
    // ---- the grid ---------------------------------------------------------------------------

    [Theory]
    [InlineData(Element.Fire, Element.Ice)]
    [InlineData(Element.Ice, Element.Fire)]
    [InlineData(Element.Thunder, Element.Water)]
    [InlineData(Element.Water, Element.Thunder)]
    [InlineData(Element.Earth, Element.Wind)]
    [InlineData(Element.Wind, Element.Earth)]
    [InlineData(Element.Holy, Element.Dark)]
    [InlineData(Element.Dark, Element.Holy)]
    public void Opposed_elements_hurt_each_other(Element attack, Element defender)
    {
        Assert.Equal(Elements.SuperEffective, Elements.Effectiveness(attack, defender));
    }

    /// <summary>
    /// The one asymmetry worth having: it stops a Fire party bulldozing a Fire floor with Fire.
    /// </summary>
    [Theory]
    [InlineData(Element.Fire)]
    [InlineData(Element.Holy)]
    [InlineData(Element.Wind)]
    public void An_element_is_resisted_by_itself(Element element)
    {
        Assert.Equal(Elements.NotVeryEffective, Elements.Effectiveness(element, element));
    }

    [Fact]
    public void Unrelated_elements_are_neutral()
    {
        Assert.Equal(Elements.Neutral, Elements.Effectiveness(Element.Fire, Element.Holy));
        Assert.Equal(Elements.Neutral, Elements.Effectiveness(Element.Earth, Element.Water));
    }

    /// <summary>A monster with no element of its own takes everything at face value.</summary>
    [Fact]
    public void A_defender_with_no_affinity_is_neutral_to_everything()
    {
        foreach (var element in Enum.GetValues<Element>())
            Assert.Equal(Elements.Neutral, Elements.Effectiveness(element, null));
    }

    // ---- the evidence ladder ----------------------------------------------------------------

    /// <summary>
    /// The strongest signal there is: nothing drinks an element it is not made of. A Bomb absorbs
    /// Fire because a Bomb is fire.
    /// </summary>
    [Fact]
    public void What_it_absorbs_outranks_everything_else()
    {
        var affinity = Elements.Affinity(
            absorbs: "Fire",
            abilities: "Blizzard, Blizzara, Ice Storm",   // says Ice, loudly
            weaknesses: "Holy");

        Assert.Equal(Element.Fire, affinity);
    }

    [Fact]
    public void Otherwise_it_is_what_it_mostly_casts()
    {
        var affinity = Elements.Affinity(null, "Blizzard, Blizzara, Fire", null);

        Assert.Equal(Element.Ice, affinity);
    }

    /// <summary>
    /// A creature with one Fire spell and one Ice spell is a mage, not a Fire monster. Resolving
    /// the tie by list order would hand out affinities on the strength of an editor's ordering.
    /// </summary>
    [Fact]
    public void An_even_split_of_spells_is_not_evidence()
    {
        Assert.Null(Elements.Affinity(null, "Fire, Blizzard", null));
    }

    [Fact]
    public void A_lone_weakness_implies_its_opposite()
    {
        Assert.Equal(Element.Fire, Elements.Affinity(null, null, "Ice"));
    }

    /// <summary>Two weaknesses point at two elements and neither is more right.</summary>
    [Fact]
    public void Several_weaknesses_imply_nothing()
    {
        Assert.Null(Elements.Affinity(null, null, "Ice, Water"));
    }

    /// <summary>
    /// Falling through the whole ladder is the safe answer, not a failure: a non-elemental monster
    /// forfeits its bonus and takes neutral damage, so a missing guess costs the player nothing.
    /// </summary>
    [Fact]
    public void No_evidence_at_all_leaves_it_non_elemental()
    {
        Assert.Null(Elements.Affinity(null, "Attack, Tackle, Scan", null));
    }

    // ---- parsing the wiki's vocabulary ---------------------------------------------------------

    /// <summary>The series writes Thunder as Lightning about as often, and means the same thing.</summary>
    [Fact]
    public void Lightning_is_Thunder()
    {
        Assert.True(Elements.TryParse("Lightning", out var element));
        Assert.Equal(Element.Thunder, element);
    }

    /// <summary>
    /// The affinity columns carry plenty that is not an element. Forcing those onto the nearest one
    /// would invent resistances out of nothing.
    /// </summary>
    [Theory]
    [InlineData("Gravity")]
    [InlineData("Instant Death")]
    [InlineData("Physical")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_an_element_is_dropped(string? value)
    {
        Assert.False(Elements.TryParse(value, out _));
    }

    [Fact]
    public void A_list_keeps_only_the_elements_it_recognises()
    {
        var parsed = Elements.Parse(Elements.Split("Fire, Gravity, Lightning, Instant Death")).ToList();

        Assert.Equal([Element.Fire, Element.Thunder], parsed);
    }
}
