using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// The generate prompt is the whole product: nobody reviews 5,500 pictures, so a wrong clause ships
/// a wrong library. These guard the two mistakes that have actually been made — asking for the
/// style the owner rejects, and leaving the sprite attached while telling the model to ignore it.
/// </summary>
public class GeneratePromptTests
{
    private static ImageGenerator.Brief Brief(string text = "A rounded violet slime.",
                                              bool standing = true, int figures = 1,
                                              bool dark = false, bool human = false) =>
        new(text, standing, figures, dark, human);

    private static ImageGenerator.Candidate Subject(string? setting = "a sunken grotto") =>
        new("monsters", 1510, "Blood Slime", "Final Fantasy IV", "monster",
            "Cutout", "A gelatinous mass.", setting, "https://example.test/1510.webp");

    // ---- the style the library is supposed to be in -------------------------------------------

    /// <summary>
    /// The target is Jack's 22-image sample of 2026-08-07 (mostly FF1-FF6 characters): clean anime
    /// linework with soft blended shading, a light pastel low-contrast palette, and a soft-focus
    /// background. It sits between two wordings that were both tried and both wrong — the original
    /// "cel shading ... bright even high-key lighting ... polished commercial trading-card art",
    /// which produced the glossy look he rejected, and set 138's "soft painterly ... visible
    /// brushwork", which overshot into loose painting.
    /// </summary>
    [Theory]
    [InlineData("cel shading")]
    [InlineData("high-key")]
    [InlineData("saturated subject colours")]
    [InlineData("polished commercial")]
    [InlineData("visible brushwork")]
    public void Does_not_ask_for_either_wording_that_missed(string banned)
    {
        Assert.DoesNotContain(banned, ImageGenerator.BuildPrompt(Subject()),
                              StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("blended")]
    [InlineData("pastel")]
    [InlineData("low contrast")]
    public void Asks_for_the_target_style(string wanted)
    {
        Assert.Contains(wanted, ImageGenerator.BuildPrompt(Subject()),
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The four drifts the scorer measures are each ruled out by name.</summary>
    [Theory]
    [InlineData("not loose watercolour")]
    [InlineData("not an unfinished sketch")]
    [InlineData("not flat cartoon cel")]
    [InlineData("not photorealistic")]
    public void Rules_out_each_measured_drift(string clause)
    {
        Assert.Contains(clause, ImageGenerator.BuildPrompt(Subject()),
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A subject floating on an empty backdrop is the single most common reason an image reads as
    /// off-style — it is what separated the accepted Arc and Luneth from the rejected Ingus and
    /// Refia, same game and same rendering. The old clause asked for exactly that, wanting
    /// "a soft low-contrast atmospheric wash — never a detailed scene".
    /// </summary>
    [Fact]
    public void Demands_a_painted_environment_rather_than_a_wash()
    {
        var prompt = ImageGenerator.BuildPrompt(Subject());

        Assert.Contains("painted environment", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never a blank, white or empty backdrop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("atmospheric wash", prompt, StringComparison.OrdinalIgnoreCase);

        // …but subordinate to the figure. "background" was the drift on 84 characters.
        Assert.Contains("never compete with it", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Falls_back_to_a_habitat_when_the_row_has_no_setting()
    {
        Assert.Contains("its habitat", ImageGenerator.BuildPrompt(Subject(setting: null)));
        Assert.Contains("its habitat", ImageGenerator.BuildPrompt(Subject(setting: "   ")));
    }

    // ---- brief mode ---------------------------------------------------------------------------

    /// <summary>
    /// The point of a brief is that the picture is not sent. Any surviving mention of an attachment
    /// is a prompt describing a request that was not made.
    /// </summary>
    [Theory]
    [InlineData("attached image")]
    [InlineData("the reference may be a low-resolution sprite")]
    [InlineData("IGNORE from the reference")]
    public void A_brief_removes_every_clause_that_needs_the_picture(string clause)
    {
        var prompt = ImageGenerator.BuildPrompt(Subject(), Brief());

        Assert.DoesNotContain(clause, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_brief_carries_the_identity_instead()
    {
        var text = "A rounded violet slime with two orange eyes and a wide grin.";
        var prompt = ImageGenerator.BuildPrompt(Subject(), Brief(text));

        Assert.Contains(text, prompt);
        Assert.Contains("named in the brief", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Without_a_brief_the_reference_clauses_stay()
    {
        var prompt = ImageGenerator.BuildPrompt(Subject());

        Assert.Contains("attached image", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low-resolution sprite", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Both modes still have to forbid the artefact and the watermark.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_modes_forbid_blockiness_and_glyphs(bool withBrief)
    {
        var prompt = ImageGenerator.BuildPrompt(Subject(), withBrief ? Brief() : null);

        Assert.Contains("no visible pixels", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No glyph of any kind", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("portrait, 3:4", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---- what the brief reports about the subject ------------------------------------------------

    /// <summary>
    /// The mid-thigh crop only means anything for one upright figure. Asking for it on an airship
    /// produced exactly that — a ship cut off at the thigh — on Phoenix and Nyunkrepf.
    /// </summary>
    [Fact]
    public void A_subject_that_is_not_a_standing_figure_is_not_cropped()
    {
        var character = new ImageGenerator.Candidate(
            "characters", 2530, "Phoenix", "Final Fantasy XIV", "character",
            "Flat", "An airship.", "the sky", "https://example.test/2530.webp");

        var ship = ImageGenerator.BuildPrompt(character, Brief(standing: false));
        var person = ImageGenerator.BuildPrompt(character, Brief(standing: true));

        Assert.DoesNotContain("cut at mid-thigh", ship, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fully visible, nothing running off any edge", ship, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cut at mid-thigh", person, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Kings of Lucis" — a crowd — came back from its brief as one swordsman and scored 85 on
    /// style, because style scoring cannot see a subject going missing. The count is stated as a
    /// requirement rather than left to the prose.
    /// </summary>
    [Fact]
    public void A_group_carries_its_figure_count_as_an_instruction()
    {
        var many = ImageGenerator.BuildPrompt(Subject(), Brief(figures: 7, standing: false));

        Assert.Contains("exactly 7 figures", many);
        Assert.Contains("Do not reduce them to one", many);
    }

    [Fact]
    public void A_single_subject_gets_no_count_clause()
    {
        Assert.DoesNotContain("COUNT:", ImageGenerator.BuildPrompt(Subject(), Brief(figures: 1)));
    }

    /// <summary>
    /// A count that contradicts the standing-figure answer is the busy reference reporting its
    /// bystanders — a battle screenshot, a party splash, onlookers behind a portrait. One upright
    /// person is not five figures, and honouring the count there replaces the character the row
    /// exists for with a crowd.
    /// </summary>
    [Fact]
    public void A_count_that_contradicts_one_standing_figure_is_not_believed()
    {
        var portrait = ImageGenerator.BuildPrompt(Subject(), Brief(figures: 5, standing: true));

        Assert.DoesNotContain("COUNT:", portrait);
        Assert.DoesNotContain("exactly 5 figures", portrait);
    }

    /// <summary>And a real group is asked for alone, not with the reference's extras beside it.</summary>
    [Fact]
    public void A_group_is_the_only_thing_in_its_picture()
    {
        Assert.Contains("Nobody else appears in the picture",
                        ImageGenerator.BuildPrompt(Subject(), Brief(figures: 3, standing: false)));
    }

    /// <summary>
    /// Jack, 2026-08-07: "Villains and demons can be dark, but keeping the style constraints." So
    /// the palette moves and nothing else does.
    /// </summary>
    [Fact]
    public void A_dark_subject_keeps_the_style_but_loses_the_pastel_palette()
    {
        var villain = ImageGenerator.BuildPrompt(Subject(), Brief(dark: true));

        Assert.Contains("low-key and sombre", villain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slightly pastel", villain, StringComparison.OrdinalIgnoreCase);

        // Dark is not grey. Left implicit, "low-key and sombre" produced a monochrome Chaos.
        Assert.Contains("FULLY COLOURED", villain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never a grey, monochrome or desaturated picture", villain,
                        StringComparison.OrdinalIgnoreCase);

        // The rest of the house style is untouched.
        Assert.Contains("blended", villain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no harsh cel bands", villain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not photorealistic", villain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_ordinary_subject_keeps_the_light_palette()
    {
        Assert.Contains("slightly pastel", ImageGenerator.BuildPrompt(Subject(), Brief(dark: false)),
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Promathia came back as a model sheet — one figure, several studies and annotations on bare
    /// paper. A catalogue needs one finished picture, in every mode.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Never_a_character_sheet_or_turnaround(bool withBrief)
    {
        var prompt = ImageGenerator.BuildPrompt(Subject(), withBrief ? Brief() : null);

        Assert.Contains("Never a character sheet, turnaround", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no repeated views of the subject", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Framing follows the subject, not the table it is filed in. Jack: "The humanoid monsters can
    /// fall into the same scorer/prompter as the characters. 7295 and 12649 for example" — Vayne
    /// Novus, a man, and a human skeleton. The test is "essentially a person": his accepted Goblin
    /// is a standing biped and is right framed whole, as a creature.
    /// </summary>
    [Fact]
    public void A_humanoid_monster_is_cropped_like_a_character()
    {
        var monster = new ImageGenerator.Candidate(
            "monsters", 7295, "Vayne Novus", "Final Fantasy XII", "monster",
            "Cutout", "A man.", "a palace", "https://example.test/7295.webp");

        var person = ImageGenerator.BuildPrompt(monster, Brief(human: true));
        var creature = ImageGenerator.BuildPrompt(monster, Brief(human: false));

        Assert.Contains("cut at mid-thigh", person, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cut at mid-thigh", creature, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fully visible, nothing running off any edge", creature,
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A person who is not standing — a bust, a mounted rider — still is not cropped.</summary>
    [Fact]
    public void Human_like_still_defers_to_the_standing_figure_test()
    {
        var monster = new ImageGenerator.Candidate(
            "monsters", 7295, "Vayne Novus", "Final Fantasy XII", "monster",
            "Cutout", "A man.", "a palace", "https://example.test/7295.webp");

        Assert.DoesNotContain("cut at mid-thigh",
                              ImageGenerator.BuildPrompt(monster, Brief(human: true, standing: false)),
                              StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A character with no brief at all keeps the crop — the old behaviour.</summary>
    [Fact]
    public void A_character_without_a_brief_is_still_cropped()
    {
        var character = new ImageGenerator.Candidate(
            "characters", 58, "Arc", "Final Fantasy III", "character",
            "LineArt", "A boy.", "ruins", "https://example.test/58.webp");

        Assert.Contains("cut at mid-thigh", ImageGenerator.BuildPrompt(character),
                        StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("figures")]
    [InlineData("single_standing_figure")]
    [InlineData("dark_subject")]
    [InlineData("human_like")]
    public void The_brief_request_asks_for_each_signal(string field)
    {
        Assert.Contains(field, ImageGenerator.BuildBriefPrompt(Subject()));
    }

    // ---- the brief request ----------------------------------------------------------------------

    /// <summary>
    /// Left to itself the describing model writes "a blocky, pixelated creature", which puts the
    /// artefact back into the illustration through the words rather than through the picture. The
    /// vocabulary has to be banned outright.
    /// </summary>
    [Theory]
    [InlineData("pixel")]
    [InlineData("sprite")]
    [InlineData("stair-step")]
    [InlineData("8-bit")]
    public void The_brief_request_bans_the_vocabulary_of_the_artefact(string word)
    {
        var ask = ImageGenerator.BuildBriefPrompt(Subject());
        var banned = ask[ask.IndexOf("Do not use", StringComparison.Ordinal)..];

        Assert.Contains(word, banned, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The request tells the describing model to ignore other characters, then used to ask how many
    /// figures the picture contained — so a screenshot with the party in it answered 5 and the count
    /// clause turned a portrait into a crowd. The question is about the subject.
    /// </summary>
    [Fact]
    public void The_figure_count_is_asked_about_the_subject_not_the_picture()
    {
        var ask = ImageGenerator.BuildBriefPrompt(Subject());

        Assert.Contains("about THE SUBJECT, never about the reference picture", ask);
        Assert.Contains("THE SUBJECT ITSELF is", ask);
        Assert.Contains("When in doubt answer 1", ask);
        Assert.DoesNotContain("how many distinct figures it contains", ask);
    }

    /// <summary>
    /// A species entry's wiki art is usually a row of palette swaps, and the model reads that
    /// honestly as "not one standing figure" and "four" — so the contradiction guard cannot catch
    /// it. Nine of the seventeen characters still drawn as groups after the 2026-08-08 redo had a
    /// lineup source. Monsters are mostly species entries, so the case is named outright.
    /// </summary>
    [Fact]
    public void The_brief_request_treats_a_variant_row_as_one_subject()
    {
        var ask = ImageGenerator.BuildBriefPrompt(Subject());

        Assert.Contains("SAME CREATURE APPEARS SEVERAL TIMES", ask);
        Assert.Contains("describe ONLY that one", ask);
        Assert.Contains("four colours of the same beast is one beast", ask);
        Assert.Contains("When in doubt answer 1", ask);
        Assert.Contains("neither does the same creature being drawn several times over", ask);
    }

    [Fact]
    public void The_brief_request_names_the_subject_and_asks_for_prose()
    {
        var ask = ImageGenerator.BuildBriefPrompt(Subject());

        Assert.Contains("Blood Slime", ask);
        Assert.Contains("Final Fantasy IV", ask);
        Assert.Contains("Prose only", ask, StringComparison.OrdinalIgnoreCase);
    }
}
