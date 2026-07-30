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
host.Services.AddScoped<DataRepair>();

var app = host.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

// --force      re-parse and overwrite existing field values instead of only filling nulls
// --repair     run the one-time cleanup of legacy names and unparsed infobox fragments
// --only=x,y   restrict the run to named stages (games, characters, monsters, cards)
var force  = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
var repair = args.Contains("--repair", StringComparer.OrdinalIgnoreCase);

var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.OrdinalIgnoreCase));
var stages = onlyArg is null
    ? null
    : onlyArg["--only=".Length..]
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

bool ShouldRun(string stage) => stages is null || stages.Contains(stage);

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

if (ShouldRun("monsters"))
    await scope.ServiceProvider.GetRequiredService<MonsterScraper>().ScrapeAsync();

if (ShouldRun("cards"))
    await scope.ServiceProvider.GetRequiredService<CardScraper>().ScrapeAsync(force);

logger.LogInformation("Scrape complete — {Time}", DateTimeOffset.UtcNow);
