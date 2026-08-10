using Microsoft.Extensions.Logging.Abstractions;
using MoogleAPI.Scraper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MoogleAPI.Tests;

/// <summary>
/// The model draws black on a pale field because it draws that reliably and draws transparency not
/// at all. Keying the field off afterwards is what lets the page decide what sits behind the shape
/// — and it has to be exact, because a silhouette is nothing but its edge.
/// </summary>
public class SilhouetteMatteTests
{
    /// <summary>A pale field with a black bar down the middle, and a soft edge between them.</summary>
    private static Image<Rgba32> TwoTone(int fieldTone = 227)
    {
        var image = new Image<Rgba32>(40, 40);

        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
            {
                // 14–25 solid figure, 12–13 and 26–27 the antialiased boundary.
                var tone = x is >= 14 and <= 25 ? 0
                         : x is 12 or 13 or 26 or 27 ? fieldTone / 2
                         : fieldTone;
                image[x, y] = new Rgba32((byte)tone, (byte)tone, (byte)tone, 255);
            }

        return image;
    }

    private static void Key(Image<Rgba32> image) =>
        ImageStore.KeyOutField(image, "gen-silhouette/characters/1.webp", NullLogger.Instance);

    [Fact]
    public void The_field_becomes_fully_transparent()
    {
        using var image = TwoTone();
        Key(image);

        Assert.Equal(0, image[0, 20].A);
        Assert.Equal(0, image[39, 20].A);
    }

    [Fact]
    public void The_figure_stays_opaque_black()
    {
        using var image = TwoTone();
        Key(image);

        var pixel = image[20, 20];
        Assert.Equal(255, pixel.A);
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    /// <summary>
    /// The reason this is a ramp and not a threshold. A silhouette is entirely edge, and a
    /// thresholded one has a staircase down every strand of hair.
    /// </summary>
    [Fact]
    public void The_boundary_keeps_its_antialiasing_as_partial_alpha()
    {
        using var image = TwoTone();
        Key(image);

        var edge = image[13, 20].A;
        Assert.InRange(edge, 40, 215);
    }

    /// <summary>
    /// Every pixel is black; only alpha carries the shape. Leaving the original colours under a
    /// transparent field is how you get a grey halo everywhere the edge is soft.
    /// </summary>
    [Fact]
    public void Nothing_keeps_a_colour_under_its_transparency()
    {
        using var image = TwoTone();
        Key(image);

        for (var x = 0; x < 40; x++)
        {
            var pixel = image[x, 20];
            Assert.Equal(0, pixel.R);
            Assert.Equal(0, pixel.G);
            Assert.Equal(0, pixel.B);
        }
    }

    /// <summary>
    /// A picture that is not two tones is not a silhouette — the model returned a shaded figure, a
    /// scene, or very nearly nothing. Keying it would smear it into something that looks
    /// deliberate; left opaque it is visibly wrong, and gets caught in review.
    /// </summary>
    [Fact]
    public void A_picture_that_is_not_two_tone_is_left_alone()
    {
        using var image = new Image<Rgba32>(40, 40);
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 40; x++)
                image[x, y] = new Rgba32(120, 124, 118, 255);   // flat mid grey, no figure

        Key(image);

        Assert.Equal(255, image[20, 20].A);
        Assert.Equal(120, image[20, 20].R);
    }

    /// <summary>The field is found from the picture rather than assumed, so its exact tone varies.</summary>
    [Theory]
    [InlineData(200)]
    [InlineData(227)]
    [InlineData(255)]
    public void The_field_is_keyed_at_whatever_tone_it_happens_to_be(int fieldTone)
    {
        using var image = TwoTone(fieldTone);
        Key(image);

        Assert.Equal(0, image[0, 20].A);
        Assert.Equal(255, image[20, 20].A);
    }
}
