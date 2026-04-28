using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.RateLimiting;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// FastEndpoints + OpenAPI via Scalar
builder.Services.AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "MoogleAPI";
            s.Version = "v1";
            s.Description = "A Final Fantasy data API — characters, monsters, and games.";
        };
    });

// HybridCache: L1 in-process + optional L2 Redis via IDistributedCache
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
});

// Partitioned rate limiting: 60 req/min anonymous, 600 req/min with X-Api-Key
builder.Services.AddApiRateLimiting();

var app = builder.Build();

app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Errors.UseProblemDetails();
});

// Scalar replaces Swagger UI — available at /scalar/v1
app.UseSwaggerGen();
app.MapScalarApiReference(options =>
{
    options.Title = "MoogleAPI";
    options.Theme = ScalarTheme.DeepSpace;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// Apply pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
