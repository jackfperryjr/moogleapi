using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MoogleAPI.Scraper;

/// <summary>What a picture actually is, judged from its pixels.</summary>
public enum ImageKind
{
    /// <summary>Subject on a transparent background — what a catalogue image should be.</summary>
    Cutout,

    /// <summary>Opaque and plain: a small palette on a solid ground.</summary>
    Flat,

    /// <summary>Mostly white with a handful of colours — a line drawing.</summary>
    LineArt,

    /// <summary>Opaque, wide and colour-rich: a picture of the game, not of the subject.</summary>
    Screenshot,

    /// <summary>Opaque with a dense palette — the subject is in there, and so is a scene.</summary>
    BusyBackground,
}

/// <summary>
/// Classifies artwork so the regeneration pass can target only what is wrong.
/// </summary>
/// <remarks>
/// One implementation deliberately: the audit that decides what to replace and the batch that
/// replaces it must agree exactly, or the batch quietly stops matching what was reviewed.
/// </remarks>
public static class ImageClassifier
{
    /// <summary>
    /// Sampling size. Small enough to be quick over thousands of files, large enough that a
    /// palette count still discriminates.
    /// </summary>
    private const int SampleEdge = 72;

    public static ImageKind Classify(Image<Rgba32> image)
    {
        // ResizeMode.Max, never Pad. Padding a 640x480 screenshot into a square adds
        // transparent bars worth about a quarter of the frame, and the transparency test then
        // reads that padding as a cut-out background — which cleared almost every screenshot
        // the first time this ran.
        using var sample = image.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(SampleEdge, SampleEdge),
            Mode = ResizeMode.Max,
        }));

        var colours = new HashSet<int>();
        int white = 0, clear = 0, total = 0;

        sample.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
                foreach (ref var p in accessor.GetRowSpan(y))
                {
                    total++;
                    if (p.A < 32) { clear++; continue; }
                    if (p is { R: > 235, G: > 235, B: > 235 }) white++;
                    colours.Add((p.R >> 3 << 10) | (p.G >> 3 << 5) | (p.B >> 3));
                }
        });

        var opaque = Math.Max(1, total - clear);
        var whiteFraction = (double)white / opaque;
        var clearFraction = (double)clear / Math.Max(1, total);
        var aspect = (double)image.Width / Math.Max(1, image.Height);

        // Ordered worst-first: a wide, opaque, busy image is a screenshot even when it is also
        // pale, and something is only line art when it is not a cut-out.
        if (clearFraction < 0.04 && aspect > 1.45 && colours.Count > 500) return ImageKind.Screenshot;
        if (clearFraction < 0.10 && whiteFraction > 0.35) return ImageKind.LineArt;
        if (clearFraction < 0.04 && colours.Count > 900) return ImageKind.BusyBackground;
        if (clearFraction > 0.10) return ImageKind.Cutout;

        return ImageKind.Flat;
    }

    /// <summary>Parses a comma-separated batch selection such as "screenshot,busy-background".</summary>
    public static HashSet<ImageKind> ParseKinds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [ImageKind.Screenshot, ImageKind.BusyBackground];

        var kinds = new HashSet<ImageKind>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = raw.Replace("-", "").Replace("_", "");
            if (Enum.TryParse<ImageKind>(normalized, ignoreCase: true, out var kind))
                kinds.Add(kind);
        }

        return kinds;
    }
}
