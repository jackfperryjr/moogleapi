using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoogleAPI.Web.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MoogleAPI.Scraper.Scrapers;

/// <summary>
/// Replaces artwork that is wrong for a catalogue — screenshots, line drawings, images with a
/// scene behind the subject — with generated illustrations in one house style.
/// </summary>
/// <remarks>
/// Generation is image-to-image: the existing artwork is sent as the reference, so the subject
/// stays itself. Generating from the name alone would invent a plausible creature instead, and
/// for the thousands of obscure enemies nobody can picture, plausible-but-wrong is worse than
/// a low-resolution sprite that is right.
/// <para>
/// Results land in <c>GeneratedImageUrl</c> and a separate <c>gen/</c> prefix. Nothing
/// overwrites <c>ImageUrl</c>, so the library can be reviewed, compared, and reverted by
/// clearing one column.
/// </para>
/// </remarks>
public class ImageGenerator(AppDbContext db, ImageStore store, ILogger<ImageGenerator> logger)
{
    /// <summary>Modest: this is a paid API with per-minute limits, and a batch is hundreds of calls.</summary>
    private const int Concurrency = 3;

    /// <summary>Generated art is the hero image now, so it keeps more resolution than a sprite needs.</summary>
    private const int MaxEdge = 1024;

    private static readonly string Model =
        Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.1-flash-lite-image";

    public async Task GenerateAsync(HashSet<ImageKind> kinds, int max, bool force, CancellationToken ct = default)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogError("GEMINI_KEY is not set — nothing to do.");
            return;
        }

        if (store.PublicBaseUrl is null)
        {
            logger.LogError("R2_PUBLIC_BASE_URL is not set; generated art would have nowhere readable to live.");
            return;
        }

        logger.LogInformation(
            "Generating for {Kinds} via {Model}, at most {Max} images.",
            string.Join(", ", kinds), Model, max);

        var candidates = await LoadCandidatesAsync(kinds, force, ct);
        logger.LogInformation("{Count} rows match this batch and have no generated replacement.", candidates.Count);

        if (candidates.Count == 0)
        {
            logger.LogWarning(
                "Nothing selected. Has the audit run? Image kinds are recorded by --only=audit, " +
                "and generation reads them rather than re-examining every image.");
            return;
        }

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MoogleAPI-Scraper/1.0)");

        var produced = new System.Collections.Concurrent.ConcurrentBag<(string Folder, int Id, string Url)>();
        var considered = 0;
        var generated = 0;
        var adopted = 0;
        var sem = new SemaphoreSlim(Concurrency);

        await Task.WhenAll(candidates.Select(async candidate =>
        {
            if (Volatile.Read(ref generated) >= max) return;

            await sem.WaitAsync(ct);
            try
            {
                if (Volatile.Read(ref generated) >= max) return;

                Interlocked.Increment(ref considered);

                var objectKey = $"gen/{candidate.Folder}/{candidate.Id}.webp";

                // Adopt rather than re-generate. Keys are derived from the row id, so art a
                // previous run produced is already at its final address — and an interrupted
                // run that stored images without recording their URLs would otherwise be paid
                // for twice.
                if (await store.ExistsAsync(objectKey, ct))
                {
                    produced.Add((candidate.Folder, candidate.Id, store.PublicUrlFor(objectKey)));
                    Interlocked.Increment(ref adopted);
                    return;
                }

                // Re-checked inside the gate: several tasks can pass the outer check at once.
                if (Interlocked.Increment(ref generated) > max) { Interlocked.Decrement(ref generated); return; }

                var reference = await DownloadAsync(http, candidate.ImageUrl, ct);
                if (reference is null) { Interlocked.Decrement(ref generated); return; }

                var art = await RequestArtAsync(http, key, candidate, reference, ct);
                if (art is null) { Interlocked.Decrement(ref generated); return; }

                var url = await store.UploadAsync(objectKey, art, MaxEdge, ct);
                if (url is null) { Interlocked.Decrement(ref generated); return; }

                produced.Add((candidate.Folder, candidate.Id, url));
                logger.LogInformation("  + {Name} ({Game}) [{Kind}]", candidate.Name, candidate.Game, candidate.Kind);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ImageFormatException)
            {
                logger.LogWarning("  ! {Name}: {Type} {Message}", candidate.Name, ex.GetType().Name, ex.Message);
            }
            finally { sem.Release(); }
        }));

        foreach (var (folder, id, url) in produced)
        {
            if (folder == "monsters")
                (await db.Monsters.FirstAsync(m => m.Id == id, ct)).GeneratedImageUrl = url;
            else
                (await db.Characters.FirstAsync(c => c.Id == id, ct)).GeneratedImageUrl = url;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "{Made} images recorded from {Considered} examined — {New} newly generated, {Adopted} already in the bucket.",
            produced.Count, considered, generated, adopted);
    }

    /// <summary>
    /// Copies generated art over the served URL. Separate from generation on purpose: the point
    /// of keeping both columns is that a batch can be looked at before it goes live.
    /// </summary>
    public async Task PromoteAsync(CancellationToken ct = default)
    {
        var monsters = await db.Monsters.Where(m => m.GeneratedImageUrl != null).ToListAsync(ct);
        foreach (var m in monsters) m.ImageUrl = m.GeneratedImageUrl;

        var characters = await db.Characters.Where(c => c.GeneratedImageUrl != null).ToListAsync(ct);
        foreach (var c in characters) c.ImageUrl = c.GeneratedImageUrl;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Promoted {Monsters} monster and {Characters} character images to the served URL.",
            monsters.Count, characters.Count);
    }

    private record Candidate(string Folder, int Id, string Name, string Game, string Subject,
                             string? Kind, string? Description, string? Setting, string ImageUrl);

    private async Task<List<Candidate>> LoadCandidatesAsync(
        HashSet<ImageKind> kinds, bool force, CancellationToken ct)
    {
        var wanted = kinds.Select(k => k.ToString()).ToList();

        // Cards are excluded outright: a Triple Triad card face is meant to be busy, it is
        // already the most uniform set in the library, and regenerating it would destroy the
        // very thing that makes it correct.
        var monsters = await db.Monsters
            .Where(m => m.ImageUrl != null
                        && m.ImageKind != null && wanted.Contains(m.ImageKind)
                        && (force || m.GeneratedImageUrl == null))
            .Include(m => m.Game)
            .Select(m => new Candidate("monsters", m.Id, m.Name, m.Game.Name, "monster",
                                       m.ImageKind, m.Description, m.Location, m.ImageUrl!))
            .ToListAsync(ct);

        var characters = await db.Characters
            .Where(c => c.ImageUrl != null
                        && c.ImageKind != null && wanted.Contains(c.ImageKind)
                        && (force || c.GeneratedImageUrl == null))
            .Include(c => c.Game)
            .Select(c => new Candidate("characters", c.Id, c.Name, c.Game.Name, "character",
                                       c.ImageKind, c.Description, c.Hometown, c.ImageUrl!))
            .ToListAsync(ct);

        return [.. monsters, .. characters];
    }

    /// <summary>Retries a transient TLS or timeout failure before abandoning a reference.</summary>
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

    private async Task<byte[]?> RequestArtAsync(
        HttpClient http, string key, Candidate c, byte[] reference, CancellationToken ct)
    {
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = BuildPrompt(c) },
                        new { inline_data = new { mime_type = "image/webp", data = Convert.ToBase64String(reference) } },
                    },
                },
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE" },
                imageConfig = new { aspectRatio = "3:4" },
            },
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await http.PostAsync(
                url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);

            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500)
            {
                await Task.Delay(TimeSpan.FromSeconds(15 * (attempt + 1)), ct);
                continue;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("  ! {Name}: HTTP {Status}", c.Name, (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(json);

            // Every step is probed rather than indexed: a safety block or filtered reply omits
            // "candidates" entirely, and GetProperty throws KeyNotFoundException on a missing
            // name. One such response used to abort a batch after all its work was finished.
            var part = default(JsonElement);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0
                && candidates[0].TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts))
            {
                part = parts.EnumerateArray().FirstOrDefault(p => p.TryGetProperty("inlineData", out _));
            }

            if (part.ValueKind == JsonValueKind.Undefined)
            {
                // A refusal or a text-only reply. Reported rather than retried: the same
                // prompt will produce the same answer.
                logger.LogWarning("  ! {Name}: no image in the response", c.Name);
                return null;
            }

            return Convert.FromBase64String(part.GetProperty("inlineData").GetProperty("data").GetString()!);
        }

        logger.LogWarning("  ! {Name}: gave up after repeated rate limiting", c.Name);
        return null;
    }

    /// <summary>
    /// One fixed instruction block with only the subject interpolated — identical phrasing
    /// across thousands of calls is what makes the results look like one set.
    /// </summary>
    private static string BuildPrompt(Candidate c)
    {
        var numeral = c.Game.Replace("Final Fantasy", "").Trim();
        if (numeral.Length == 0) numeral = "I";

        var setting = string.IsNullOrWhiteSpace(c.Setting) ? "its habitat" : c.Setting;

        return $"""
            Illustrate a single Final Fantasy subject as trading-card art.

            SUBJECT: "{c.Name}" — a {c.Kind} from {c.Game}.
            {c.Description}

            Use the attached image as the definitive visual reference for what the subject looks like.

            PRESERVE: silhouette, proportions, colour palette, and every distinguishing feature — horns, wings, limbs, armour, weapons, markings. It must remain recognisably the same {c.Kind}. Do not redesign it or invent features absent from the reference.

            IGNORE from the reference: menus, health bars, damage numbers, spell effects, other characters, and any scenery. Those are artefacts of a screenshot, not the subject.

            STYLE: clean modern anime-influenced digital illustration. Crisp confident linework, cel shading with soft gradient falloff, bright even high-key lighting, saturated subject colours. Polished commercial trading-card art.

            COMPOSITION: the entire subject is inside the frame in a three-quarter view — every limb, wing and tail fully visible, nothing running off any edge. It fills most, but not all, of the picture. Behind it a soft low-contrast atmospheric wash suggesting {setting} — never a detailed scene. Set into that background, one very large faint Roman numeral "{numeral}", low contrast and partly hidden behind the subject, as a watermark motif.

            DO NOT INCLUDE: any text, letters, words, logos, signatures, or UI. The Roman numeral is the only permitted glyph.

            CRITICAL — the artwork is full-bleed: the illustration runs edge to edge with no card frame, no border, no outline, no rounded corners, no inner bevel, no matting and no margin of any kind. Do not draw a card. Draw only the picture that would go inside one.

            FORMAT: portrait, 3:4.
            """;
    }
}
