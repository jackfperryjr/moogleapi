using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// A silhouette has exactly one job — be recognisable as a shape and as nothing else — and there
/// are two ways to fail it, both measured on the real artwork before the stage was written:
/// interior detail leaves the character readable, and the reference's painted scene names the game
/// before the first guess. Both are prohibitions, so both are invisible in a picture that happens
/// to come out right, and neither would survive a careless edit to the prompt.
/// </summary>
public class SilhouettePromptTests
{
    private static ImageGenerator.Candidate Character() =>
        new("characters", 471, "Vivi Ornitier", "Final Fantasy IX", "character",
            "Cutout", "A black mage.", "Treno", "https://example.test/gen/characters/471.webp");

    [Fact]
    public void Names_the_subject_it_is_reducing()
    {
        var prompt = ImageGenerator.BuildSilhouettePrompt(Character());

        Assert.Contains("Vivi Ornitier", prompt);
        Assert.Contains("Final Fantasy IX", prompt);
    }

    /// <summary>
    /// The shape is the whole puzzle value. A silhouette redrawn in a new pose is a picture of
    /// somebody else, and the parts that break the outline — Vivi's hat, Cloud's sword — are
    /// exactly the parts a player reads.
    /// </summary>
    [Fact]
    public void Copies_the_reference_outline_rather_than_reinterpreting_it()
    {
        var prompt = ImageGenerator.BuildSilhouettePrompt(Character());

        Assert.Contains("Same pose, same crop, same framing", prompt);
        Assert.Contains("breaks the figure's edge there breaks it here in the same", prompt);
        Assert.Contains("Do not redraw, restyle, straighten, simplify or re-pose", prompt);
    }

    /// <summary>
    /// Failure one: darkening the artwork not quite far enough leaves the character plainly
    /// recognisable. A diffusion model will shade "a silhouette" unless told several ways over
    /// not to.
    /// </summary>
    [Theory]
    [InlineData("No face, no eyes")]
    [InlineData("no shading")]
    [InlineData("no gradient")]
    [InlineData("no transparency")]
    [InlineData("same single flat black")]
    [InlineData("Nothing inside the shape identifies who it is")]
    public void Forbids_every_kind_of_interior_detail(string clause)
    {
        Assert.Contains(clause, ImageGenerator.BuildSilhouettePrompt(Character()),
                        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Failure two, and the worse one: Kupodle is narrowed by guessing the game, so an FFVII
    /// street or FFXIII architecture behind the shape gives that away for free. The reference is a
    /// full painted scene, so discarding it has to be said rather than left unsaid.
    /// </summary>
    [Fact]
    public void Discards_the_scene_the_reference_carries()
    {
        var prompt = ImageGenerator.BuildSilhouettePrompt(Character());

        Assert.Contains("THE REFERENCE'S BACKGROUND IS DISCARDED ENTIRELY", prompt);
        Assert.Contains("no scene, no room, no sky, no horizon", prompt);
        Assert.Contains("Exactly two tones", prompt);

        // A cast shadow reads as a floor, and a floor is a place.
        Assert.Contains("no cast shadow", prompt);
    }

    /// <summary>
    /// The house style is the opposite of what this stage wants, and the clause that carries it is
    /// the one most likely to be copied across by hand.
    /// </summary>
    [Theory]
    [InlineData("painted environment")]
    [InlineData("pastel")]
    [InlineData("blended")]
    [InlineData("anime-style")]
    public void Does_not_ask_for_the_house_style(string clause)
    {
        Assert.DoesNotContain(clause, ImageGenerator.BuildSilhouettePrompt(Character()),
                              StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The frame it hangs in is 3:4 and the portrait that replaces it is 3:4. A square shape would
    /// letterbox against its own reveal.
    /// </summary>
    [Fact]
    public void Keeps_the_frame_the_rest_of_the_library_uses()
    {
        var prompt = ImageGenerator.BuildSilhouettePrompt(Character());

        Assert.Contains("portrait, 3:4", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No glyph of any kind", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
