using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MoogleAPI.Web.Infrastructure.Arena;
using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Images;
using MoogleAPI.Web.Infrastructure.Middleware;
using MoogleAPI.Web.Infrastructure.Puzzles;
using MoogleAPI.Web.Infrastructure.RateLimiting;
using MoogleAPI.Web.Infrastructure.Wiki;
using Scalar.AspNetCore;
using System.Security.Claims;

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

// Partitioned rate limiting: 60 req/min anonymous, 600 req/min with a recognized X-Api-Key.
// The allowlist is what makes the premium tier mean anything — without it the header was
// self-service. No startup validation: an empty list legitimately means nobody has premium.
builder.Services.Configure<PremiumKeyOptions>(builder.Configuration.GetSection(PremiumKeyOptions.SectionName));
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddApiRateLimiting();

// Daily puzzle seeding. Validated at startup rather than on first request: an empty secret
// still yields a deterministic seed, so booting without one would silently ship a puzzle
// whose future answers anyone can compute.
builder.Services.AddOptions<DailyPuzzleOptions>()
    .Bind(builder.Configuration.GetSection(DailyPuzzleOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret),
        "DailyPuzzle:Secret is required. Set DailyPuzzle__Secret (env var) or use user-secrets.")
    .ValidateOnStart();
builder.Services.AddSingleton<DailyPuzzle>();
builder.Services.AddScoped<DailyCharacterSelector>();
builder.Services.AddScoped<BattlePool>();
builder.Services.AddScoped<ClimbBuilder>();
builder.Services.AddScoped<ArenaBuilder>();

// ── Dashboard support ─────────────────────────────────────────────────────────
// Final Fantasy Wiki, now reachable one page at a time through the dashboard's import. The
// scraper that drove it in bulk is gone; these are the same parsers, asked for one article.
builder.Services.AddHttpClient<WikiClient>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("MoogleAPI/1.0 (dashboard import)");
    c.Timeout = TimeSpan.FromSeconds(30);
});

// Image uploads. Optional by design: with no R2 credentials the upload endpoint answers 503 and
// every other part of the site — including the rest of the dashboard — runs. The options are
// captured here rather than registered, because "not configured" is a legitimate state and a
// null service registration is not.
var imageUploadOptions = ImageUploadOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(sp =>
    new ImageUploadStore(imageUploadOptions, sp.GetRequiredService<ILogger<ImageUploadStore>>()));

// Google OAuth — credentials from user-secrets (dev) or env vars (prod):
//   Authentication__Google__ClientId / Authentication__Google__ClientSecret
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/signin";
    options.AccessDeniedPath = "/denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // Signed in, but not as the owner: /denied is a page, so an API caller gets the status code.
    options.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx, StatusCodes.Status403Forbidden);
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;

    // Google is the default challenge scheme, so an unauthenticated request to a protected
    // endpoint bounces off Google's consent screen rather than the cookie handler's login path —
    // which means this, not CookieAuthenticationEvents.OnRedirectToLogin, is where the dashboard's
    // fetch calls have to be answered. They cannot follow a cross-origin 302: it lands as an
    // opaque network error instead of "your session ended". Give them the status code.
    options.Events.OnRedirectToAuthorizationEndpoint = ctx => ApiAware(ctx, StatusCodes.Status401Unauthorized);
});

// Browsers navigating get the redirect; anything under /api gets the bare status.
static Task ApiAware<TOptions>(RedirectContext<TOptions> ctx, int apiStatusCode)
    where TOptions : AuthenticationSchemeOptions
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
        ctx.Response.StatusCode = apiStatusCode;
    else
        ctx.Response.Redirect(ctx.RedirectUri);
    return Task.CompletedTask;
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Dashboard", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim(ClaimTypes.Email, "jackfperryjr@gmail.com"));

var app = builder.Build();

app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseDefaultFiles();

// Static assets are cached by rule rather than left to whatever sits in front of the app.
//
// Nothing here set Cache-Control, so Cloudflare applied its own four-hour default to every
// script and stylesheet — and the games' JavaScript is not decoration, it is the client half of
// the combat model. The server picks a run's waves with the same arithmetic the browser resolves
// them with, so a browser holding yesterday's game.js is not showing a stale page, it is playing
// a different game: after the damage curve changed in feature set 115, a cached client still
// needed four hits to kill a seven-HP Final Fantasy III Goblin that the server had balanced
// around two.
//
// So markup, scripts and styles revalidate on every request — an ETag makes that a 304 and a few
// bytes — while images, which are content-addressed by row id and never change in place, keep a
// long lifetime.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        var mustRevalidate =
            name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

        ctx.Context.Response.GetTypedHeaders().CacheControl = mustRevalidate
            ? new CacheControlHeaderValue { NoCache = true }
            : new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(30) };
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Log all /api/* requests asynchronously — never blocks the response
app.UseMiddleware<RequestLoggingMiddleware>();

// GET requests have no body — strip Content-Type so FastEndpoints doesn't
// attempt JSON deserialization when clients (e.g. Postman) send the header anyway.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsGet(ctx.Request.Method))
        ctx.Request.ContentType = null;
    await next();
});

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Errors.UseProblemDetails();
});

// Scalar replaces Swagger UI — available at /scalar/v1
app.UseSwaggerGen();
app.MapScalarApiReference(options =>
{
    options.Title = "moogleAPI";
    options.Theme = ScalarTheme.DeepSpace;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.WithFavicon("/favicon.ico");
    // FastEndpoints.Swagger (NSwag) serves the spec here, not the ASP.NET Core default
    options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
});

// ── Health ────────────────────────────────────────────────────────────────────
// Point Railway's Healthcheck Path at this. Railway holds the old container in
// service until the new one answers here, which is what turns a deploy from a
// hard restart into a handover — see cloudflare/maintenance-worker/README.md.
//
// Deliberately liveness only, not readiness. Answering at all already proves the
// interesting part: EF migrations run to completion before app.RunAsync() below,
// so nothing binds the port until the schema is current. Probing the database here
// too would mean a transient Neon hiccup could fail an otherwise good deploy.
//
// Rate limiting off — the probe hits repeatedly from one address and would
// otherwise eat into the 60/min anonymous partition and start 429ing itself.
// HEAD as well as GET: uptime monitors default to HEAD, and MapGet alone answers
// those with 405, which reads as an outage to the very thing meant to detect one.
app.MapMethods("/health", [HttpMethods.Get, HttpMethods.Head],
        () => Results.Ok(new { status = "healthy" }))
   .DisableRateLimiting()
   .ExcludeFromDescription();

// The Gauntlet was renamed to Kupo Climb. The old path is in shared results and whatever people
// bookmarked, so it moves permanently rather than 404ing — the page itself is gone, not the game.
app.MapGet("/the-gauntlet", () => Results.Redirect("/kupo-climb", permanent: true))
   .ExcludeFromDescription();

// ── Private pages ─────────────────────────────────────────────────────────────
// Served through protected endpoints rather than static files so auth is enforced, and all
// excluded from the OpenAPI document: they are HTML for one signed-in person, and listing them
// in the public reference only invites 403s from people who thought they had found an endpoint.
//
// /dashboard browses the catalogue itself — characters, monsters and games in tabbed tables,
// so the data can be checked after a scrape without opening a database client.
// /stats is the request-log analytics that used to live at /dashboard.
app.MapGet("/dashboard", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.ContentRootPath, "Dashboard", "index.html"), "text/html"))
    .RequireAuthorization("Dashboard")
    .ExcludeFromDescription();

app.MapGet("/stats", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.ContentRootPath, "Stats", "index.html"), "text/html"))
    .RequireAuthorization("Dashboard")
    .ExcludeFromDescription();

// Trigger Google sign-in and redirect back to dashboard on success
app.MapGet("/signin", () =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/dashboard" },
        [GoogleDefaults.AuthenticationScheme]))
    .ExcludeFromDescription();

// Sign out and return to landing page
app.MapPost("/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization()
  .ExcludeFromDescription();

app.MapGet("/denied", () =>
    Results.Text("Access denied — this dashboard is private.", "text/plain", statusCode: 403))
    .ExcludeFromDescription();

// Apply pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
