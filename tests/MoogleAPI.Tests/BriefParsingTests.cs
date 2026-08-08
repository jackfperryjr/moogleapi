using System.Text.Json;
using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// The brief response is parsed in the middle of a run that may be a thousand rows long, so a
/// reply the parser did not expect must cost one row rather than the batch.
/// </summary>
/// <remarks>
/// It cost a batch once. On 2026-08-08 a 1,000-character run died at 975 on a
/// <c>KeyNotFoundException</c> from indexing "candidates" on a filtered reply — the same failure
/// <c>RequestArtAsync</c> had already been hardened against in this very file, with a comment
/// explaining it. Promotion survived only because it sits in a <c>finally</c>.
/// </remarks>
public class BriefParsingTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Reads_a_flag_that_is_present()
    {
        Assert.True(ImageGenerator.Flag(Json("""{"human_like": true}"""), "human_like", false));
        Assert.False(ImageGenerator.Flag(Json("""{"human_like": false}"""), "human_like", true));
    }

    /// <summary>
    /// The schema marks the flags required and the model still sometimes omits them. Each default
    /// is the answer that changes the least: framed whole, ordinary palette, not a person.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"other": 1}""")]
    [InlineData("""{"human_like": null}""")]
    [InlineData("""{"human_like": "yes"}""")]
    public void A_missing_or_unusable_flag_falls_back(string body)
    {
        Assert.True(ImageGenerator.Flag(Json(body), "human_like", true));
        Assert.False(ImageGenerator.Flag(Json(body), "human_like", false));
    }

    [Fact]
    public void Reads_a_number_that_is_present()
    {
        Assert.Equal(7, ImageGenerator.Number(Json("""{"figures": 7}"""), "figures", 1));
    }

    /// <summary>A count arriving as a string is still a count.</summary>
    [Fact]
    public void Reads_a_number_sent_as_a_string()
    {
        Assert.Equal(4, ImageGenerator.Number(Json("""{"figures": "4"}"""), "figures", 1));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"figures": null}""")]
    [InlineData("""{"figures": "several"}""")]
    [InlineData("""{"figures": 1.5}""")]
    public void A_missing_or_unusable_number_falls_back(string body)
    {
        Assert.Equal(1, ImageGenerator.Number(Json(body), "figures", 1));
    }

    /// <summary>Neither reader may throw, whatever arrives — that is the whole point.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"figures": [1,2], "human_like": {"a": 1}}""")]
    [InlineData("""{"figures": "", "human_like": ""}""")]
    public void Neither_reader_throws_on_anything(string body)
    {
        var root = Json(body);

        var ex = Record.Exception(() =>
        {
            ImageGenerator.Flag(root, "human_like", false);
            ImageGenerator.Number(root, "figures", 1);
        });

        Assert.Null(ex);
    }
}
