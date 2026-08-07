using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoogleAPI.Scraper;
using MoogleAPI.Scraper.Scrapers;
using MoogleAPI.Web.Infrastructure.Data;

// The artwork tool. It keeps the project name only because renaming one is noisier than the
// clarity would be worth.
//
// The stages that first populated the catalogue are gone. They were a seeder: they ran, they
// finished, and what they produced has since been corrected and curated by hand. They also
// matched rows by page name rather than by id, so a re-run could only undo that work — deleted
// rows returned, renamed rows arrived a second time, and hand-scored popularity was overwritten
// on sight. Rows are added through the dashboard now, one at a time, by a person.
//
// What remains is the part that has not finished, because it is about pixels rather than facts:
// copying art into R2, classifying what each picture actually is, and paying Gemini to replace
// the ones that are wrong for a catalogue.

var host = Host.CreateApplicationBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? host.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("No connection string found. Set CONNECTION_STRING env var or appsettings.json.");

host.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// Image hosting is no longer optional: every remaining stage writes to the bucket.
var imageOptions = ImageStoreOptions.FromEnvironment();
if (imageOptions is not null)
{
    host.Services.AddSingleton(imageOptions);
    host.Services.AddHttpClient<ImageStore>(c =>
    {
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoogleAPI-Images/1.0");
        c.Timeout = TimeSpan.FromSeconds(60);
    });
    host.Services.AddScoped<ImageScraper>();
    host.Services.AddScoped<ImageGenerator>();
    host.Services.AddScoped<ImageAuditor>();
    host.Services.AddScoped<ImageReverter>();
}

var app = host.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

if (imageOptions is null)
{
    logger.LogError(
        "Image storage is not configured — set R2_ACCOUNT_ID, ACCESS_KEY and SECRET_KEY. " +
        "Every stage in this tool writes to the bucket, so there is nothing to run without it.");
    return 1;
}

// --only=x,y   restrict the run to named stages (images, audit, generate, promote, unpromote)
// --force      re-copy or re-classify rows that have already been done
// --kinds=x,y  which classes of bad image the generate stage replaces
// --max=N      hard ceiling on images generated in one run, so a mistake cannot empty a budget
// --ids=m1,c2  confine generate and unpromote to named rows; --ids=@file reads a long list
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);

var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.OrdinalIgnoreCase));
var stages = onlyArg is null
    ? null
    : onlyArg["--only=".Length..]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

var kindsArg = args.FirstOrDefault(a => a.StartsWith("--kinds=", StringComparison.OrdinalIgnoreCase))
    ?["--kinds=".Length..];

var maxImages = int.TryParse(
    args.FirstOrDefault(a => a.StartsWith("--max=", StringComparison.OrdinalIgnoreCase))?["--max=".Length..],
    out var parsedMax) ? parsedMax : 25;

// Parsed before anything runs, and allowed to stop the run. A malformed entry in a list of
// hundreds must not be read as "no restriction" — that is the difference between withdrawing
// 250 images and withdrawing every one in the library.
IdSelection? ids;
try
{
    ids = IdSelection.Parse(
        args.FirstOrDefault(a => a.StartsWith("--ids=", StringComparison.OrdinalIgnoreCase))?["--ids=".Length..]);
}
catch (Exception ex) when (ex is ArgumentException or IOException)
{
    logger.LogError("{Message}", ex.Message);
    return 1;
}

if (ids is not null) logger.LogInformation("Restricted by --ids to {Selection}.", ids);

await using var scope = app.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

logger.LogInformation("Applying migrations...");
await db.Database.MigrateAsync();

logger.LogInformation(
    "Starting image run — {Time} (force={Force}, stages={Stages})",
    DateTimeOffset.UtcNow, force, stages is null ? "images" : string.Join(",", stages));

// Copies whatever art the rows already point at into our own bucket. It is the default stage
// because it is the only harmless one: it never chooses art, it only moves what is there.
if (stages is null || stages.Contains("images"))
    await scope.ServiceProvider.GetRequiredService<ImageScraper>().ScrapeAsync(force);

// Records what each stored image is, so the generate stage can pick its batch by query.
if (stages is not null && stages.Contains("audit"))
    await scope.ServiceProvider.GetRequiredService<ImageAuditor>().AuditAsync(force);

// Replaces artwork that is wrong for a catalogue. Costs money per image, so it is capped and
// never runs by default — it has to be asked for by name.
//
// Generating implies promoting: art that was paid for and left sitting in GeneratedImageUrl
// helps nobody, and the review-before-live step this used to require was never the thing
// standing between a bad image and the catalogue — `unpromote` is, and it still puts the
// originals back and deletes the generated art.
//
// The promote is in a finally rather than on the next line, because "next line" is exactly what
// failed. Set 131 made generating imply promoting as two sequential statements, so anything that
// threw inside generation skipped it — and on 2026-08-07 one deleted row did precisely that,
// stranding 1,676 generated images at roughly $62 until promote was run by hand. Whatever a run
// managed to buy before it died still goes live.
var generating = stages is not null && stages.Contains("generate");

if (generating)
{
    var generator = scope.ServiceProvider.GetRequiredService<ImageGenerator>();
    try
    {
        await generator.GenerateAsync(ImageClassifier.ParseKinds(kindsArg), maxImages, force, ids);
    }
    finally
    {
        // Best effort, and never allowed to replace the failure that got us here: a promote
        // that throws inside a finally would swallow the original exception and report the
        // wrong cause.
        try
        {
            await generator.PromoteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Promote failed after generation. Rerun with --only=promote — it is free and idempotent.");
        }
    }
}

// Promotion on its own, for art from a run that predates the automatic promote above.
//
// Worth knowing: this promotes *every* row holding generated art, not only the rows a run
// produced. Anything deliberately generated and left unpromoted earlier goes live too.
else if (stages is not null && stages.Contains("promote"))
    await scope.ServiceProvider.GetRequiredService<ImageGenerator>().PromoteAsync();

// Puts the served URLs back on the copied originals and deletes the generated art. Destructive
// and asked for by name, like generate — and for the same reason, since what it throws away was
// paid for. Both halves are one stage on purpose: reverting without deleting leaves the next
// batch to adopt the very images that were just rejected.
if (stages is not null && stages.Contains("unpromote"))
    await scope.ServiceProvider.GetRequiredService<ImageReverter>().RevertAsync(ids);

logger.LogInformation("Image run complete — {Time}", DateTimeOffset.UtcNow);
return 0;
