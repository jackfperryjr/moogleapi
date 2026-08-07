using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// The generate prompt is the whole product: nobody reviews 5,500 pictures, so a wrong clause ships
/// a wrong library. These guard the two mistakes that have actually been made — asking for the
/// style the owner rejects, and leaving the sprite attached while telling the model to ignore it.
/// </summary>
public class GeneratePromptTests
{
    private static ImageGenerator.Candidate Subject(string? setting = "a sunken grotto") =>
        new("monsters", 1510, "Blood Slime", "Final Fantasy IV", "monster",
            "Cutout", "A gelatinous mass.", setting, "https://example.test/1510.webp");

    // ---- the style the library is supposed to be in -------------------------------------------

    /// <summary>
    /// The clause used to ask for "clean modern anime-influenced digital illustration ... cel
    /// shading ... bright even high-key lighting". That is the look Jack rejected by name in Refia,
    /// Dio and Cloud, and the painterly images in the library happened in spite of it.
    /// </summary>
    [Theory]
    [InlineData("anime-influenced")]
    [InlineData("cel shading")]
    [InlineData("high-key")]
    [InlineData("saturated subject colours")]
    public void Does_not_ask_for_the_rejected_glossy_style(string banned)
    {
        Assert.DoesNotContain(banned, ImageGenerator.BuildPrompt(Subject()),
                              StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("soft painterly")]
    [InlineData("visible brushwork")]
    [InlineData("muted")]
    public void Asks_for_the_painterly_house_style(string wanted)
    {
        Assert.Contains(wanted, ImageGenerator.BuildPrompt(Subject()),
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

        Assert.Contains("fully painted environment", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never a blank, white or empty backdrop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("atmospheric wash", prompt, StringComparison.OrdinalIgnoreCase);
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
        var prompt = ImageGenerator.BuildPrompt(Subject(), brief: "A rounded violet slime.");

        Assert.DoesNotContain(clause, prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_brief_carries_the_identity_instead()
    {
        var brief = "A rounded violet slime with two orange eyes and a wide grin.";
        var prompt = ImageGenerator.BuildPrompt(Subject(), brief);

        Assert.Contains(brief, prompt);
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
    [InlineData(null)]
    [InlineData("A rounded violet slime.")]
    public void Both_modes_forbid_blockiness_and_glyphs(string? brief)
    {
        var prompt = ImageGenerator.BuildPrompt(Subject(), brief);

        Assert.Contains("no visible pixels", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No glyph of any kind", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("portrait, 3:4", prompt, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void The_brief_request_names_the_subject_and_asks_for_prose()
    {
        var ask = ImageGenerator.BuildBriefPrompt(Subject());

        Assert.Contains("Blood Slime", ask);
        Assert.Contains("Final Fantasy IV", ask);
        Assert.Contains("Prose only", ask, StringComparison.OrdinalIgnoreCase);
    }
}
