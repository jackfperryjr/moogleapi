using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoogleAPI.Scraper;
using MoogleAPI.Scraper.Scrapers;
using MoogleAPI.Web.Infrastructure.Data;

var host = Host.CreateApplicationBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? host.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("No connection string found. Set CONNECTION_STRING env var or appsettings.json.");

host.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

host.Services.AddHttpClient<WikiClient>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MoogleAPI-Scraper/1.0");
    c.Timeout = TimeSpan.FromSeconds(30);
});

host.Services.AddScoped<GameSeeder>();
host.Services.AddScoped<CharacterScraper>();
host.Services.AddScoped<MonsterScraper>();
host.Services.AddScoped<CardScraper>();
host.Services.AddScoped<PlayableScraper>();
host.Services.AddScoped<DataRepair>();

// Image hosting is optional: without R2 credentials every other stage still runs, and the
// API keeps serving the wiki's URLs.
var imageOptions = ImageStoreOptions.FromEnvironment();
if (imageOptions is not null)
{
    host.Services.AddSingleton(imageOptions);
    host.Services.AddHttpClient<ImageStore>(c =>
    {
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MoogleAPI-Scraper/1.0");
        c.Timeout = TimeSpan.FromSeconds(60);
    });
    host.Services.AddScoped<ImageScraper>();
    host.Services.AddScoped<ImageGenerator>();
    host.Services.AddScoped<ImageAuditor>();
    host.Services.AddScoped<ImageReverter>();
}

var app = host.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

// --force      re-parse and overwrite existing field values instead of only filling nulls
// --repair     run the one-time cleanup of legacy names and unparsed infobox fragments
// --only=x,y   restrict the run to named stages (games, characters, playable, monsters, cards,
//              images, audit, generate, promote, unpromote)
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
var repair = args.Contains("--repair", StringComparer.OrdinalIgnoreCase);

var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.OrdinalIgnoreCase));
var stages = onlyArg is null
    ? null
    : onlyArg["--only=".Length..]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

bool ShouldRun(string stage) => stages is null || stages.Contains(stage);

// --kinds=x,y  which classes of bad image the generate stage replaces
// --max=N      hard ceiling on images generated in one run, so a mistake cannot empty a budget
var kindsArg = args.FirstOrDefault(a => a.StartsWith("--kinds=", StringComparison.OrdinalIgnoreCase))
    ?["--kinds=".Length..];

var maxImages = int.TryParse(
    args.FirstOrDefault(a => a.StartsWith("--max=", StringComparison.OrdinalIgnoreCase))?["--max=".Length..],
    out var parsedMax) ? parsedMax : 25;

await using var scope = app.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

logger.LogInformation("Applying migrations...");
await db.Database.MigrateAsync();

logger.LogInformation(
    "Starting scrape run — {Time} (force={Force}, repair={Repair}, stages={Stages})",
    DateTimeOffset.UtcNow, force, repair, stages is null ? "all" : string.Join(",", stages));

// Must precede the character scrape: it renames rows so the scraper can match them again.
if (repair)
    await scope.ServiceProvider.GetRequiredService<DataRepair>().RepairAsync();

if (ShouldRun("games"))
    await scope.ServiceProvider.GetRequiredService<GameSeeder>().SeedAsync();

if (ShouldRun("characters"))
    await scope.ServiceProvider.GetRequiredService<CharacterScraper>().ScrapeAsync(force);

// After characters: it flags rows the character scrape has to have created first.
if (ShouldRun("playable"))
    await scope.ServiceProvider.GetRequiredService<PlayableScraper>().ScrapeAsync();

if (ShouldRun("monsters"))
    await scope.ServiceProvider.GetRequiredService<MonsterScraper>().ScrapeAsync(force);

if (ShouldRun("cards"))
    await scope.ServiceProvider.GetRequiredService<CardScraper>().ScrapeAsync(force);

// Last: it copies whatever art the earlier stages settled on.
if (ShouldRun("images"))
{
    if (imageOptions is null)
        logger.LogWarning("Skipping images — set R2_ACCOUNT_ID, ACCESS_KEY and SECRET_KEY to enable it.");
    else
        await scope.ServiceProvider.GetRequiredService<ImageScraper>().ScrapeAsync(force);
}

// Records what each stored image is, so the generate stage can pick its batch by query.
if (stages is not null && stages.Contains("audit"))
    await scope.ServiceProvider.GetRequiredService<ImageAuditor>().AuditAsync(force);

// Replaces artwork that is wrong for a catalogue. Costs money per image, so it is capped and
// never runs as part of "all stages" — it has to be asked for by name.
if (stages is not null && stages.Contains("generate"))
{
    if (imageOptions is null)
        logger.LogWarning("Skipping generate — image storage is not configured.");
    else
        await scope.ServiceProvider.GetRequiredService<ImageGenerator>()
            .GenerateAsync(ImageClassifier.ParseKinds(kindsArg), maxImages, force);
}

// Copies generated art over the served URL, once it has been looked at.
if (stages is not null && stages.Contains("promote"))
{
    if (imageOptions is null)
        logger.LogWarning("Skipping promote — image storage is not configured.");
    else
        await scope.ServiceProvider.GetRequiredService<ImageGenerator>().PromoteAsync();
}

// Puts the served URLs back on the copied originals and deletes the generated art. Destructive
// and asked for by name, like generate — and for the same reason, since what it throws away was
// paid for. Both halves are one stage on purpose: reverting without deleting leaves the next
// batch to adopt the very images that were just rejected.
if (stages is not null && stages.Contains("unpromote"))
{
    if (imageOptions is null)
        logger.LogWarning("Skipping unpromote — image storage is not configured.");
    else
        await scope.ServiceProvider.GetRequiredService<ImageReverter>().RevertAsync();
}

logger.LogInformation("Scrape complete — {Time}", DateTimeOffset.UtcNow);
