using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Re-frames generated character art from a full-body figure to a three-quarter crop.
/// </summary>
/// <remarks>
/// Jack picked Arc (c58) and Luneth (c63) as the framing he wants — cut around mid-thigh, subject
/// filling the canvas — against Refia, Cloud, Dio and Elena, which are head-to-toe with dead space
/// above and below: <em>"I like the images zoomed a bit, like Arc and Luneth."</em>
/// <para>
/// The cause is the generate prompt, which demands "the entire subject is inside the frame … every
/// limb, wing and tail fully visible". Feature set 139 splits that clause so characters are no
/// longer asked for a full-body figure, but that only helps art generated afterwards. This stage
/// fixes what is already in the bucket.
/// </para>
/// <para>
/// It crops rather than regenerates on purpose. The images concerned are ones he likes the look
/// of — only the framing is wrong — and regenerating would spend money to change the style too.
/// A crop costs nothing and cannot alter what it does not touch.
/// </para>
/// <para>
/// Monsters are excluded. Whole-creature framing is right for them: a cropped tail or wing loses
/// the thing a bestiary picture is for.
/// </para>
/// </remarks>
public class ImageRecropper(AppDbContext db, ImageStore store, ILogger<ImageRecropper> logger)
{
    /// <summary>Where the pristine full-body image goes before the cropped one replaces it.</summary>
    private const string BackupPrefix = "gen-uncropped";

    private const int MaxEdge = 1024;

    /// <summary>Portrait 3:4, the shape every generated image is already in.</summary>
    private const double AspectRatio = 0.75;

    private static readonly string Model =
        Environment.GetEnvironmentVariable("GEMINI_TEXT_MODEL") ?? "gemini-3.5-flash-lite";

    public async Task RecropAsync(IdSelection? only = null, bool force = false,
                                  int max = int.MaxValue, CancellationToken ct = default)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogError("GEMINI_KEY is not set — the framing has to be judged before anything is cut.");
            return;
        }

        if (store.PublicBaseUrl is null)
        {
            logger.LogError("R2_PUBLIC_BASE_URL is not set.");
            return;
        }

        var rows = await db.Characters
            .Where(c => c.GeneratedImageUrl != null
                        && (only == null || only.Characters.Contains(c.Id)))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        logger.LogInformation(
            "Examining {Count} character images for full-body framing (max {Max}).", rows.Count, max);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MoogleAPI-Scraper/1.0)");

        var examined = 0;
        var cropped = 0;
        var alreadyTight = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            if (cropped >= max) break;
            ct.ThrowIfCancellationRequested();

            var objectKey = $"gen/characters/{row.Id}.webp";
            var backupKey = $"{BackupPrefix}/characters/{row.Id}.webp";

            // A backup that already exists means this row has been through the stage. Re-copying
            // would overwrite the pristine full-body image with the cropped one and make the
            // change permanent, so the guard is what keeps a second run from destroying the
            // ability to undo the first.
            var hasBackup = await store.ExistsAsync(backupKey, ct);
            if (hasBackup && !force)
            {
                skipped++;
                continue;
            }

            // Cache-busted: keys derive from the row id, so the edge is holding the previous
            // version of this exact address and will serve it back without this.
            var url = $"{store.PublicUrlFor(objectKey)}?cb={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var bytes = await DownloadAsync(http, url, ct);
            if (bytes is null) { skipped++; continue; }

            examined++;

            var framing = await JudgeAsync(http, key, row.Name, bytes, ct);
            if (framing is null) { skipped++; continue; }

            if (!framing.IsFullBody)
            {
                alreadyTight++;
                continue;
            }

            var recropped = Crop(bytes, framing.CutAt);
            if (recropped is null) { skipped++; continue; }

            // The original goes somewhere safe before anything overwrites it. If this fails the
            // row is left alone: an un-backed-up crop is not worth the picture it replaces.
            //
            // The bytes already in hand are what gets stored, rather than re-fetching the public
            // URL. Re-fetching went wrong immediately: that URL is edge-cached, so the copy came
            // back as a previous version of the image while the crop was made from the current
            // one, and the "original" saved for Elena was a different picture from the one being
            // replaced. Backing up what was actually read removes the race entirely.
            if (!hasBackup && await store.UploadAsync(backupKey, bytes, MaxEdge, ct) is null)
            {
                logger.LogWarning("  ! c{Id} {Name} — could not back up; left uncropped.", row.Id, row.Name);
                skipped++;
                continue;
            }

            if (await store.UploadAsync(objectKey, recropped, MaxEdge, ct) is null)
            {
                skipped++;
                continue;
            }

            cropped++;
            logger.LogInformation(
                "  c{Id} {Name} — cropped at {Cut:P0}. Original kept at {Backup}.",
                row.Id, row.Name, framing.CutAt, backupKey);
        }

        logger.LogInformation(
            "Recrop complete — {Cropped} cropped, {Tight} already framed tightly, {Skipped} skipped, "
            + "{Examined} examined. Originals are under {Prefix}/ and the URLs are unchanged, so "
            + "nothing in the database moved.",
            cropped, alreadyTight, skipped, examined, BackupPrefix);
    }

    /// <summary>
    /// Puts the uncropped originals back and forgets the backups, undoing <see cref="RecropAsync"/>.
    /// </summary>
    /// <remarks>
    /// The backup is fetched cache-busted, for the same reason the crop is: these keys are edge
    /// cached, and restoring a stale copy of the original would defeat the point of having one.
    /// The backup is deleted last, so a failure part-way leaves a row restorable rather than
    /// stranded with a cropped image and nothing to go back to.
    /// </remarks>
    public async Task UncropAsync(IdSelection? only = null, CancellationToken ct = default)
    {
        var rows = await db.Characters
            .Where(c => c.GeneratedImageUrl != null
                        && (only == null || only.Characters.Contains(c.Id)))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var restored = 0;
        var missing = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var backupKey = $"{BackupPrefix}/characters/{row.Id}.webp";
            if (!await store.ExistsAsync(backupKey, ct)) { missing++; continue; }

            var bust = $"?cb={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            if (await store.CopyAsync(store.PublicUrlFor(backupKey) + bust,
                                      $"gen/characters/{row.Id}.webp", ct) is null)
            {
                logger.LogWarning("  ! c{Id} {Name} — could not restore.", row.Id, row.Name);
                continue;
            }

            await store.DeleteAsync(backupKey, ct);
            restored++;
            logger.LogInformation("  c{Id} {Name} — uncropped original restored.", row.Id, row.Name);
        }

        logger.LogInformation(
            "Uncrop complete — {Restored} restored, {Missing} had no backup (never cropped).",
            restored, missing);
    }

    private sealed record Framing(bool IsFullBody, double CutAt);

    /// <summary>
    /// Asks where the figure sits in the frame. Judged per image rather than applied as a fixed
    /// ratio, because "mid-thigh" is a different fraction of the canvas for a figure that already
    /// fills it than for one standing small in the middle.
    /// </summary>
    private async Task<Framing?> JudgeAsync(
        HttpClient http, string key, string name, byte[] image, CancellationToken ct)
    {
        var prompt = $$"""
            This is generated character art for "{{name}}", in portrait 3:4.

            Decide how the figure is framed.

            "full_body": true ONLY when the whole figure is visible head to toe — both feet, or the
            bottom hem of a robe, clearly inside the frame — AND there is obvious wasted space, so
            the figure looks small and centred with room above the head or below the feet.

            Answer false whenever the picture is ALREADY well framed: if the figure is cut off by
            the bottom edge at the thigh, knee or waist; if it is a bust or head-and-shoulders; if
            it already fills the frame from top to bottom with only a little headroom. A picture
            that is already close enough must be left alone — cropping it again would cut into the
            torso. Also answer false when the subject is not a standing figure at all: a vehicle,
            an object, a mounted rider, a group of people, or an abstract shape.

            "cut_at": only meaningful when full_body is true. The height, as a fraction of the
            image from the top, at which to cut so the figure ends at MID-THIGH — below the hips,
            well ABOVE the knees. Err on the generous side: keeping a little too much leg is much
            better than cutting into the hips or waist. Between 0.60 and 0.95.

            "head_top": the fraction from the top where the top of the head or hat begins.

            JSON only: {"full_body": true, "cut_at": 0.0, "head_top": 0.0}
            """;

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = "image/webp", data = Convert.ToBase64String(image) } },
                        new { text = prompt },
                    },
                },
            },
            generationConfig = new
            {
                temperature = 0.0,
                maxOutputTokens = 1024,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        full_body = new { type = "boolean" },
                        cut_at = new { type = "number" },
                        head_top = new { type = "number" },
                    },
                    required = new[] { "full_body", "cut_at", "head_top" },
                },
            },
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var response = await http.PostAsync(
                    url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode is 429 or >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), ct);
                        continue;
                    }

                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var text = doc.RootElement.GetProperty("candidates")[0]
                    .GetProperty("content").GetProperty("parts")
                    .EnumerateArray()
                    .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                if (text is null) return null;

                using var parsed = JsonDocument.Parse(text);
                var full = parsed.RootElement.GetProperty("full_body").GetBoolean();
                var cut = parsed.RootElement.GetProperty("cut_at").GetDouble();

                return new Framing(full, cut);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                          or JsonException or KeyNotFoundException)
            {
                if (attempt == 2) return null;
                await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Cuts the picture at <paramref name="cutAt"/> and re-frames to 3:4, anchored at the top so
    /// the head keeps its headroom. Returns null when the crop would not actually zoom in.
    /// </summary>
    internal static byte[]? Crop(byte[] bytes, double cutAt)
    {
        // A model that answers 0.99 has not found the knees, and cropping on it would trim a few
        // pixels for nothing. The lower bound is 0.60 rather than 0.45 because the first run cut
        // Refia at 0.58 and took her off at the waist: below about 0.6 the answer is far more
        // likely to be a bad estimate than a genuinely leggy composition, and the cost of being
        // wrong is asymmetric — too much leg is a slightly loose picture, too little is a ruined
        // one.
        if (cutAt is < 0.60 or > 0.95) return null;

        try
        {
            using var image = Image.Load(bytes);

            var height = (int)Math.Round(image.Height * cutAt);
            var width = (int)Math.Round(height * AspectRatio);

            // Wider than the source: the cut is too shallow to give a 3:4 frame without adding
            // canvas. Fall back to full width and take the height that 3:4 allows.
            if (width > image.Width)
            {
                width = image.Width;
                height = (int)Math.Round(width / AspectRatio);
            }

            if (height >= image.Height && width >= image.Width) return null;

            var x = Math.Max(0, (image.Width - width) / 2);
            height = Math.Min(height, image.Height);
            width = Math.Min(width, image.Width - x);

            image.Mutate(c => c.Crop(new Rectangle(x, 0, width, height)));

            // Back up to the library's standard long edge, so a cropped image is not quietly
            // smaller than every other one beside it.
            if (image.Height < MaxEdge)
            {
                image.Mutate(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(MaxEdge, MaxEdge),
                    Mode = ResizeMode.Max,
                }));
            }

            using var output = new MemoryStream();
            image.SaveAsWebp(output);
            return output.ToArray();
        }
        catch (ImageFormatException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DownloadAsync(HttpClient http, string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await http.GetByteArrayAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt == 2) return null;
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }

        return null;
    }
}
