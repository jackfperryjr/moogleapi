using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// A rebase runs as part of the default image stage — the one described in the code as the only
/// harmless one — and rewrites the served URL of every copied row without touching the bucket.
/// It answered "the copied original" for all of them, so the next domain move would have taken
/// every generated image in the library out of service and put wiki art back, while reporting
/// itself as having repointed some pictures.
/// </summary>
public class RebaseTests
{
    [Theory]
    [InlineData("https://images.moogleapi.com/gen/characters/471.webp", "characters", 471, "gen/characters/471.webp")]
    [InlineData("https://images.moogleapi.com/gen/monsters/8584.webp", "monsters", 8584, "gen/monsters/8584.webp")]
    public void A_row_serving_generated_art_is_rebased_to_the_generated_object(
        string current, string folder, int id, string expected) =>
        Assert.Equal(expected, ImageScraper.ServedKey(current, folder, id));

    [Theory]
    [InlineData("https://images.moogleapi.com/characters/471.webp", "characters", 471, "characters/471.webp")]
    [InlineData("https://images.moogleapi.com/monsters/8584.webp", "monsters", 8584, "monsters/8584.webp")]
    [InlineData("https://images.moogleapi.com/cards/12.webp", "cards", 12, "cards/12.webp")]
    public void A_row_serving_its_copied_original_is_rebased_to_the_copied_original(
        string current, string folder, int id, string expected) =>
        Assert.Equal(expected, ImageScraper.ServedKey(current, folder, id));

    /// <summary>
    /// The old r2.dev address, mid-move. The host is exactly what a rebase exists to change, so
    /// the decision has to come off the path.
    /// </summary>
    [Fact]
    public void The_layer_is_read_from_the_path_not_the_host() =>
        Assert.Equal(
            "gen/characters/471.webp",
            ImageScraper.ServedKey("https://pub-abc123.r2.dev/gen/characters/471.webp", "characters", 471));

    /// <summary>
    /// <c>monsters/12.webp</c> is a suffix of <c>gen/monsters/12.webp</c>, so a match that did not
    /// anchor on the separator would file every generated row under the plain key.
    /// </summary>
    [Fact]
    public void The_plain_key_does_not_match_inside_the_generated_key() =>
        Assert.Equal("gen/monsters/12.webp", ImageScraper.ServedKey(
            "https://images.moogleapi.com/gen/monsters/12.webp", "monsters", 12));

    /// <summary>
    /// A row can only be rebased onto an address in our own bucket, and the copied original is the
    /// address the copy stage would have used. Nothing else is a safe guess.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("https://static.wikia.nocookie.net/finalfantasy/images/e/e4/Amano_Imp.jpg")]
    public void Anything_else_falls_back_to_the_copied_original(string? current) =>
        Assert.Equal("characters/194.webp", ImageScraper.ServedKey(current, "characters", 194));
}
