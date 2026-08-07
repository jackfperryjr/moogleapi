using MoogleAPI.Scraper.Scrapers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MoogleAPI.Tests;

/// <summary>
/// The crop overwrites art that was paid for, so the arithmetic has to be right and the refusals
/// have to hold: a crop that does not actually zoom in is a re-encode of a picture for nothing.
/// </summary>
public class RecropTests
{
    private static byte[] Portrait(int w = 768, int h = 1024)
    {
        using var image = new Image<Rgba32>(w, h);
        using var ms = new MemoryStream();
        image.SaveAsWebp(ms);
        return ms.ToArray();
    }

    private static (int W, int H) SizeOf(byte[] bytes)
    {
        using var image = Image.Load(bytes);
        return (image.Width, image.Height);
    }

    [Fact]
    public void Cropping_at_mid_thigh_returns_a_three_four_frame()
    {
        var (w, h) = SizeOf(ImageRecropper.Crop(Portrait(), 0.72)!);

        Assert.Equal(0.75, (double)w / h, 2);
    }

    /// <summary>The point of the stage: the subject has to end up bigger than it was.</summary>
    [Fact]
    public void Cropping_actually_zooms_in()
    {
        var cropped = ImageRecropper.Crop(Portrait(768, 1024), 0.72)!;
        var (w, _) = SizeOf(cropped);

        // Same long edge as before, but a narrower slice of the original is filling it.
        var slice = (int)Math.Round(1024 * 0.72 * 0.75);
        Assert.True(w > slice, "the crop should be scaled back up to the library's long edge");
    }

    [Fact]
    public void Cropped_art_keeps_the_libraries_long_edge()
    {
        var (_, h) = SizeOf(ImageRecropper.Crop(Portrait(), 0.72)!);

        Assert.Equal(1024, h);
    }

    /// <summary>
    /// A model that answers 0.99 has not found the knees; one that answers 0.2 would cut through
    /// the chest. Neither is a crop worth making.
    /// </summary>
    /// <remarks>
    /// The floor is 0.60, not 0.45. The first live run cut Refia at 0.58 and took her off at the
    /// waist — below about 0.6 the number is far more likely to be a bad estimate than a genuinely
    /// leggy composition, and the cost is asymmetric: too much leg is a loose picture, too little
    /// is a ruined one.
    /// </remarks>
    [Theory]
    [InlineData(0.99)]
    [InlineData(0.96)]
    [InlineData(0.58)]
    [InlineData(0.44)]
    [InlineData(0.1)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Refuses_a_cut_line_outside_the_believable_range(double cutAt)
    {
        Assert.Null(ImageRecropper.Crop(Portrait(), cutAt));
    }

    [Theory]
    [InlineData(0.60)]
    [InlineData(0.72)]
    [InlineData(0.95)]
    public void Accepts_the_believable_range(double cutAt)
    {
        Assert.NotNull(ImageRecropper.Crop(Portrait(), cutAt));
    }

    /// <summary>
    /// A shallow cut on an already-narrow image cannot make 3:4 without inventing canvas. Falling
    /// back to full width is right; silently padding would not be.
    /// </summary>
    [Fact]
    public void A_narrow_source_falls_back_to_full_width()
    {
        var cropped = ImageRecropper.Crop(Portrait(400, 1024), 0.95);

        Assert.NotNull(cropped);
        var (w, h) = SizeOf(cropped!);
        Assert.Equal(0.75, (double)w / h, 2);
    }

    [Fact]
    public void Undecodable_bytes_are_refused_rather_than_thrown()
    {
        Assert.Null(ImageRecropper.Crop([1, 2, 3, 4], 0.72));
    }
}
