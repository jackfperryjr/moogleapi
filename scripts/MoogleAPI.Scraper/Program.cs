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

var app = host.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

await using var scope = app.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

logger.LogInformation("Applying migrations...");
await db.Database.MigrateAsync();

logger.LogInformation("Starting scrape run — {Time}", DateTimeOffset.UtcNow);

await scope.ServiceProvider.GetRequiredService<GameSeeder>().SeedAsync();
await scope.ServiceProvider.GetRequiredService<CharacterScraper>().ScrapeAsync();
await scope.ServiceProvider.GetRequiredService<MonsterScraper>().ScrapeAsync();

logger.LogInformation("Scrape complete — {Time}", DateTimeOffset.UtcNow);
