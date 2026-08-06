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
/// Results land in <c>GeneratedImageUrl</c> and a separate <c>gen/</c> prefix, then go live
/// immediately: <c>generate</c> promotes what it produced. The original is still kept in its
/// own column, which is what lets <c>unpromote</c> put it back and delete the replacement.
/// </para>
/// </remarks>
public class ImageGenerator(AppDbContext db, ImageStore store, ILogger<ImageGenerator> logger)
{
    /// <summary>
    /// One request at a time. A batch is hundreds of calls against a per-minute limit, and
    /// parallelism here does not buy throughput — it buys collisions.
    /// </summary>
    /// <remarks>
    /// At three, a 700-image batch produced 304 and abandoned 225 to repeated rate limiting,
    /// taking two hours to do it. The three workers were competing for the same per-minute
    /// allowance and losing, and every abandoned image had still spent its requests against the
    /// daily cap — which is counted in calls, not pictures, so the waste came directly out of
    /// the day's yield. Serially there is nothing to collide with, and the pace is set by
    /// <see cref="MaxAttempts"/> backing off against what the API asks for.
    /// </remarks>
    private const int Concurrency = 1;

    /// <summary>
    /// Tries per image. Higher than it was, because a retry that waits long enough is cheaper
    /// than an abandoned image: both spend the request, only one comes back with a picture.
    /// </summary>
    private const int MaxAttempts = 5;

    /// <summary>Ceiling on a single wait, so one absurd hint cannot stall a whole batch.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Rows written to the database per save, so a batch that dies partway keeps what it earned.
    /// </summary>
    /// <remarks>
    /// The save used to happen once, after the last image. A 400-image batch on 2026-08-03 ran
    /// for close to the runner's six-hour ceiling, and anything killed at that ceiling lost every
    /// <c>GeneratedImageUrl</c> for art that had already been generated, paid for and uploaded.
    /// Nothing was ever bought twice — keys derive from the row id, so the next run adopts the
    /// objects back for free — but recovering bookkeeping that was already done cost a whole
    /// extra run. Flushing as it goes caps that loss at the last few images instead.
    /// </remarks>
    private const int FlushEvery = 25;

    /// <summary>Generated art is the hero image now, so it keeps more resolution than a sprite needs.</summary>
    private const int MaxEdge = 1024;

    private static readonly string Model =
        Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.1-flash-lite-image";

    /// <summary>Raised once the first unrecognised 429 body has been shown, so it is shown once.</summary>
    private int _loggedUnknownRefusal;

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

        // Images made but not yet written to their rows. Drained every FlushEvery, so an
        // interrupted run leaves at most that many objects in the bucket with a null column.
        var pending = new System.Collections.Concurrent.ConcurrentQueue<(string Folder, int Id, string Url)>();
        var recorded = 0;
        var sinceFlush = 0;
        // The context is not thread-safe. At a concurrency of one the worker gate already
        // serialises everything, but the flush must not depend on that staying true.
        var dbGate = new SemaphoreSlim(1);
        var considered = 0;
        var generated = 0;
        var adopted = 0;
        // Raised the first time the API refuses in a way no later row can survive. Everything
        // still in flight finishes; nothing new starts. Without it the run would keep pulling
        // fresh candidates, because a failed row decrements the counter and the batch is nowhere
        // near its ceiling.
        var quotaSpent = 0;
        // What stopped it, kept so the closing summary can name the cause and the remedy.
        GenerationHaltedException? halt = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var sem = new SemaphoreSlim(Concurrency);

        async Task FlushAsync()
        {
            await dbGate.WaitAsync(ct);
            try
            {
                var written = 0;
                while (pending.TryDequeue(out var row))
                {
                    if (row.Folder == "monsters")
                        (await db.Monsters.FirstAsync(m => m.Id == row.Id, ct)).GeneratedImageUrl = row.Url;
                    else
                        (await db.Characters.FirstAsync(c => c.Id == row.Id, ct)).GeneratedImageUrl = row.Url;
                    written++;
                }

                if (written == 0) return;

                await db.SaveChangesAsync(ct);
                Interlocked.Add(ref recorded, written);
            }
            finally { dbGate.Release(); }
        }

        // Counting the flush trigger rather than testing the queue length keeps a flush that
        // races with an enqueue from writing the same row twice: the queue is the only owner.
        async Task RecordAsync(string folder, int id, string url)
        {
            pending.Enqueue((folder, id, url));
            if (Interlocked.Increment(ref sinceFlush) < FlushEvery) return;

            Interlocked.Exchange(ref sinceFlush, 0);
            await FlushAsync();
        }

        await Task.WhenAll(candidates.Select(async candidate =>
        {
            if (Volatile.Read(ref generated) >= max || Volatile.Read(ref quotaSpent) == 1) return;

            await sem.WaitAsync(ct);
            try
            {
                if (Volatile.Read(ref generated) >= max || Volatile.Read(ref quotaSpent) == 1) return;

                Interlocked.Increment(ref considered);

                var objectKey = $"gen/{candidate.Folder}/{candidate.Id}.webp";

                // Adopt rather than re-generate. Keys are derived from the row id, so art a
                // previous run produced is already at its final address — and an interrupted
                // run that stored images without recording their URLs would otherwise be paid
                // for twice.
                if (await store.ExistsAsync(objectKey, ct))
                {
                    await RecordAsync(candidate.Folder, candidate.Id, store.PublicUrlFor(objectKey));
                    Interlocked.Increment(ref adopted);
                    return;
                }

                // Re-checked inside the gate: several tasks can pass the outer check at once.
                if (Interlocked.Increment(ref generated) > max) { Interlocked.Decrement(ref generated); return; }

                var reference = await DownloadAsync(http, candidate.ImageUrl, ct);
                if (reference is null) { Interlocked.Decrement(ref generated); return; }

                byte[]? art;
                try
                {
                    art = await RequestArtAsync(http, key, candidate, reference, ct);
                }
                catch (GenerationHaltedException ex)
                {
                    Interlocked.Decrement(ref generated);

                    // Logged once however many tasks trip over the wall at the same moment.
                    if (Interlocked.Exchange(ref quotaSpent, 1) == 0)
                    {
                        halt = ex;
                        logger.LogError(
                            "Stopping {Elapsed:hh\\:mm\\:ss} in, after {Made} images: {Reason} for {Model}. {Detail}",
                            clock.Elapsed, Volatile.Read(ref recorded) + pending.Count, ex.Reason, Model, ex.Detail);
                    }

                    return;
                }

                if (art is null) { Interlocked.Decrement(ref generated); return; }

                var url = await store.UploadAsync(objectKey, art, MaxEdge, ct);
                if (url is null) { Interlocked.Decrement(ref generated); return; }

                await RecordAsync(candidate.Folder, candidate.Id, url);
                logger.LogInformation("  + {Name} ({Game}) [{Kind}]", candidate.Name, candidate.Game, candidate.Kind);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ImageFormatException)
            {
                logger.LogWarning("  ! {Name}: {Type} {Message}", candidate.Name, ex.GetType().Name, ex.Message);
            }
            finally { sem.Release(); }
        }));

        // Drains whatever the last partial group left. Runs even when the quota cut the batch
        // short: those images were paid for and uploaded, and leaving the column null would make
        // the next run buy them again.
        await FlushAsync();
        logger.LogInformation(
            "{Made} images recorded from {Considered} examined in {Elapsed:hh\\:mm\\:ss} — " +
            "{New} newly generated, {Adopted} already in the bucket.",
            recorded, considered, clock.Elapsed, generated, adopted);

        if (halt is not null)
        {
            // The remedy differs by cause, and getting it wrong wastes hours: waiting out a
            // reset that no clock will bring does nothing, and topping up a healthy account
            // does nothing either.
            var remedy = halt.Kind switch
            {
                RefusalKind.CreditsExhausted =>
                    "Top up the prepaid balance at https://ai.studio/projects — no amount of waiting clears this.",
                RefusalKind.DailyQuota =>
                    "Rerun the same command once the quota resets.",
                _ => "Rerun the same command.",
            };

            logger.LogWarning(
                "Stopped because {Reason}, with {Untouched} of {Total} candidates never attempted. {Remedy} " +
                "They have no GeneratedImageUrl, so they are still selected.",
                halt.Reason, candidates.Count - considered, candidates.Count, remedy);
        }
    }

    /// <summary>
    /// Copies generated art over the served URL. Runs automatically after <c>generate</c>, and
    /// can still be asked for by name to pick up art from an earlier run. Both columns are kept
    /// even so — <c>ImageReverter</c> needs the original to put back.
    /// </summary>
    /// <remarks>
    /// Promotes every row holding generated art, not just the current batch.
    /// </remarks>
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

        // Carried out of the loop so the give-up line can say what the API actually objected to,
        // rather than asserting "rate limiting" for every refusal it never inspected.
        var lastRefusal = "no refusal recorded";

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            using var response = await http.PostAsync(
                url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);

            if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500)
            {
                var wait = FallbackBackoff(attempt);

                // A 429 is two unrelated problems sharing one status code, and only the body
                // separates them. A per-minute burst limit clears while we sleep. The per-day
                // quota resets in hours, so backing off cannot help: the attempts spend more
                // requests to learn the same thing, and then the run does it again on the next
                // row. Batch 3's first attempt burned ~300 requests that way and produced nothing.
                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests)
                {
                    var payload = await response.Content.ReadAsStringAsync(ct);
                    var kind = ClassifyRefusal(payload, out var detail);
                    lastRefusal = detail;

                    if (kind is RefusalKind.CreditsExhausted)
                        throw new GenerationHaltedException(kind, "the account is out of prepaid credit", detail);

                    if (kind is RefusalKind.DailyQuota)
                        throw new GenerationHaltedException(kind, "the daily request quota is spent", detail);

                    // Anything we could not name gets shown once, in full. Every refusal used to
                    // be read, classified and thrown away, so a 429 the classifier did not know
                    // about was indistinguishable in the log from ordinary throttling.
                    if (Interlocked.Exchange(ref _loggedUnknownRefusal, 1) == 0)
                        logger.LogWarning("First unrecognised 429 of this run, in full: {Body}", Truncate(payload));

                    // The response says how long it wants us gone. It was being read only to
                    // phrase the daily-quota message and thrown away for burst limits, which
                    // left the wait a guess — and a guess that came back too early spends a
                    // request to be told the same thing again. Never shorter than the ladder,
                    // so repeated failures still lengthen.
                    if (RetryDelayFrom(payload) is { } advised && advised > wait)
                        wait = advised;
                }

                await Task.Delay(wait < MaxBackoff ? wait : MaxBackoff, ct);
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

        logger.LogWarning(
            "  ! {Name}: gave up after {Attempts} refusals — last was: {Detail}",
            c.Name, MaxAttempts, lastRefusal);
        return null;
    }

    /// <summary>Keeps one pathological error body from burying the rest of the log.</summary>
    private static string Truncate(string body) =>
        body.Length <= 800 ? body : string.Concat(body.AsSpan(0, 800), " …[truncated]");

    /// <summary>How long to wait before attempt <paramref name="attempt"/> + 1, absent a hint.</summary>
    private static TimeSpan FallbackBackoff(int attempt) => TimeSpan.FromSeconds(20 * (attempt + 1));

    /// <summary>
    /// The delay the API asks for in a 429 body, if it gives one.
    /// </summary>
    /// <remarks>
    /// Google returns <c>retryDelay</c> on a rate-limited response, and honouring it is strictly
    /// better than a ladder we invented: it is the only party that knows when the window opens.
    /// </remarks>
    internal static TimeSpan? RetryDelayFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entry in details.EnumerateArray())
            {
                // "21s" — seconds with a trailing s, per google.protobuf.Duration.
                if (entry.TryGetProperty("retryDelay", out var delay)
                    && delay.GetString() is { } raw
                    && double.TryParse(raw.TrimEnd('s'), out var seconds)
                    && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sorts a 429 into the three things it can mean, so the caller knows whether to wait, stop,
    /// or send someone to the billing page.
    /// </summary>
    /// <remarks>
    /// Order matters. Credit exhaustion is checked first because it is the only one of the three
    /// that carries no <c>details</c> array — matching it later would mean falling through the
    /// quota checks that look for one.
    /// </remarks>
    internal static RefusalKind ClassifyRefusal(string body, out string detail)
    {
        if (IsCreditsExhausted(body))
        {
            detail = MessageFrom(body) ?? "the account is out of prepaid credit";
            return RefusalKind.CreditsExhausted;
        }

        if (IsDailyQuota(body, out var reset))
        {
            detail = reset;
            return RefusalKind.DailyQuota;
        }

        detail = MessageFrom(body) ?? "no message given";
        return RefusalKind.Burst;
    }

    /// <summary>
    /// Whether a 429 says the account has run out of prepaid credit.
    /// </summary>
    /// <remarks>
    /// Keyed on the message text, unlike <see cref="IsDailyQuota"/>, because this body has no
    /// <c>details</c> array at all: no quotaId to match, no retryDelay to honour. Matched against
    /// the live response on 2026-08-03 — "Your prepayment credits are depleted. Please go to AI
    /// Studio at https://ai.studio/projects to manage your project and billing."
    /// <para>
    /// Prose is free to change, and a miss here is expensive: before this existed, a depleted
    /// account read as an ordinary burst limit and a batch spent the runner's whole six-hour
    /// ceiling backing off politely, attempting ~70 images and generating none. That is why an
    /// unrecognised 429 body is now logged in full rather than silently retried.
    /// </para>
    /// </remarks>
    internal static bool IsCreditsExhausted(string body)
    {
        if (MessageFrom(body) is not { } message) return false;

        return message.Contains("prepayment credit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credits are depleted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The human-readable <c>error.message</c>, if the body has one.</summary>
    private static string? MessageFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            return doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a 429 body is the per-day quota rather than a per-minute burst limit.
    /// </summary>
    /// <remarks>
    /// Keyed on <c>quotaId</c> containing <c>PerDay</c> rather than on the human-readable
    /// message, which is prose and free to change. Anything unrecognised is treated as a burst
    /// limit: guessing "burst" costs one wasted retry, guessing "daily" would abandon a run that
    /// could have finished.
    /// </remarks>
    internal static bool IsDailyQuota(string body, out string detail)
    {
        detail = "no reset time given";

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("status", out var status)
                || status.GetString() != "RESOURCE_EXHAUSTED"
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var daily = false;

            foreach (var entry in details.EnumerateArray())
            {
                if (entry.TryGetProperty("violations", out var violations)
                    && violations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var violation in violations.EnumerateArray())
                    {
                        if (violation.TryGetProperty("quotaId", out var id)
                            && id.GetString() is { } quotaId
                            && quotaId.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                        {
                            daily = true;
                        }
                    }
                }

                // "72193s" — seconds with a trailing s, per google.protobuf.Duration.
                if (entry.TryGetProperty("retryDelay", out var delay)
                    && delay.GetString() is { } raw
                    && double.TryParse(raw.TrimEnd('s'), out var seconds))
                {
                    detail = $"resets in about {TimeSpan.FromSeconds(seconds):h\\h\\ m\\m}";
                }
            }

            return daily;
        }
        catch (JsonException)
        {
            // An unparseable body is not evidence of a daily quota. Let the retry handle it.
            return false;
        }
    }

    /// <summary>
    /// One fixed instruction block with only the subject interpolated — identical phrasing
    /// across thousands of calls is what makes the results look like one set.
    /// </summary>
    /// <remarks>
    /// The prompt asks for no glyph of any kind, and the game's Roman numeral is not to be put
    /// back into it. It was here for two batches and never worked: a diffusion model draws the
    /// shape of text rather than spelling it, and Roman numerals are the worst case for that,
    /// being runs of identical strokes whose serifs merge. Asked for "I" it drew two stems and a
    /// whole batch came back reading II; III and VIII smeared into a block. Spelling the numeral
    /// out character by character with its length stated up front — which is what used to live
    /// here — narrowed the failure without removing it, and it cost part of every prompt and
    /// part of every picture.
    /// <para>
    /// If the numeral is wanted again, draw it after generation rather than asking for it. The
    /// project already has ImageSharp, so compositing one is deterministic, adjustable without
    /// paying for a new image, and correct every time.
    /// </para>
    /// <para>
    /// The RESOLUTION and GEOMETRY clauses are aimed at a failure the earlier wording caused
    /// rather than prevented. Sprite-sourced art came back with smooth linework and clean cel
    /// shading — the anti-pixel instruction worked — but with the forms themselves still made of
    /// squares: bones as chains of boxes, ribcages as stacked rectangles, a sword blade stepping
    /// down in right angles. Two lines were asking for it. <c>PRESERVE</c> led with "silhouette",
    /// and RESOLUTION called the reference "a specification of shape" — so the most compliant
    /// thing the model could do with a 48-pixel Bloodbones was to trace its grid at high
    /// resolution. Shape fidelity is now stated as identity and proportion rather than contour,
    /// and the blockiness is named as geometry to discard, not merely as texture to smooth.
    /// Verified against gen/monsters/15.webp, which is what prompted the change.
    /// </para>
    /// </remarks>
    private static string BuildPrompt(Candidate c)
    {
        var setting = string.IsNullOrWhiteSpace(c.Setting) ? "its habitat" : c.Setting;

        return $"""
            Illustrate a single Final Fantasy subject as trading-card art.

            SUBJECT: "{c.Name}" — a {c.Kind} from {c.Game}.
            {c.Description}

            Use the attached image as the definitive visual reference for what the subject looks like.

            PRESERVE: proportions, colour palette, and every distinguishing feature — horns, wings, limbs, armour, weapons, markings. It must remain recognisably the same {c.Kind}. Do not redesign it or invent features absent from the reference.

            IGNORE from the reference: menus, health bars, damage numbers, spell effects, other characters, and any scenery. Those are artefacts of a screenshot, not the subject.

            RESOLUTION: the reference may be a low-resolution sprite. Its blockiness is a hardware limit of the era it was drawn for, not a design choice. At that size every edge was forced onto a square grid, so its outline is an artefact of that grid and not the subject's true shape — read the reference for which parts exist, their colours and their proportions, never for its contour and never as a rendering style. Draw at full fidelity: smooth confident linework and clean edges, with no visible pixels, no dithering, no stair-stepping and no blocky shading anywhere in the picture.

            GEOMETRY: nothing in the picture is built from squares, rectangles, bars or stacked boxes, and no organic form carries a right angle or a stepped, notched or staircase edge. Every diagonal is a true diagonal and every curve a true curve. Bones taper and have rounded ends and real joints, limbs vary in thickness along their length, and blades have a continuous straight edge and a point. Where the reference shows a form as a rectangle, that is the grid approximating something rounded, tapered or angled — draw the real thing.

            STYLE: clean modern anime-influenced digital illustration. Crisp confident linework, cel shading with soft gradient falloff, bright even high-key lighting, saturated subject colours. Polished commercial trading-card art.

            COMPOSITION: the entire subject is inside the frame in a three-quarter view — every limb, wing and tail fully visible, nothing running off any edge. It fills most, but not all, of the picture. Behind it a soft low-contrast atmospheric wash suggesting {setting} — never a detailed scene, and nothing else set into it.

            DO NOT INCLUDE: any text, letters, numbers, numerals, words, logos, signatures, watermarks or UI. No glyph of any kind appears anywhere in the picture.

            CRITICAL — the artwork is full-bleed: the illustration runs edge to edge with no card frame, no border, no outline, no rounded corners, no inner bevel, no matting and no margin of any kind. Do not draw a card. Draw only the picture that would go inside one.

            FORMAT: portrait, 3:4.
            """;
    }
}

/// <summary>
/// What a 429 actually means. One status code covers three unrelated situations, and only the
/// response body separates them.
/// </summary>
public enum RefusalKind
{
    /// <summary>A per-minute limit. It clears while we wait, so retrying is the right answer.</summary>
    Burst,

    /// <summary>
    /// The per-day request allowance. It resets in hours, so no later row in this run can
    /// succeed either and every further attempt spends a request to learn the same thing.
    /// </summary>
    DailyQuota,

    /// <summary>
    /// The account has no prepaid credit left. Nothing but a payment clears it, so a run that
    /// keeps trying will never produce an image however long it is given.
    /// </summary>
    CreditsExhausted,
}

/// <summary>
/// Generation cannot continue. Distinct from an ordinary failed row because it ends the whole
/// run: no later row can succeed either, and every attempt still costs a request.
/// </summary>
public class GenerationHaltedException(RefusalKind kind, string reason, string detail)
    : Exception($"{reason} — {detail}")
{
    public RefusalKind Kind { get; } = kind;

    /// <summary>Why the run stopped, as a phrase that completes "Stopping: …".</summary>
    public string Reason { get; } = reason;

    /// <summary>What the API said, or when the allowance returns.</summary>
    public string Detail { get; } = detail;
}
