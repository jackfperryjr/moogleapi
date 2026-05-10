using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Middleware;
using MoogleAPI.Web.Infrastructure.RateLimiting;
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

// Partitioned rate limiting: 60 req/min anonymous, 600 req/min with X-Api-Key
builder.Services.AddApiRateLimiting();

// Google OAuth — credentials from user-secrets (dev) or env vars (prod):
//   Authentication__Google__ClientId / Authentication__Google__ClientSecret
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath         = "/signin";
    options.AccessDeniedPath  = "/denied";
    options.ExpireTimeSpan    = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // Return 401 for AJAX dashboard API calls instead of redirecting
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/dashboard/api"))
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else
            ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddGoogle(options =>
{
    options.ClientId     = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Dashboard", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim(ClaimTypes.Email, "jackfperryjr@gmail.com"));

var app = builder.Build();

app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

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

// ── Dashboard routes ──────────────────────────────────────────────────────────
// Served through a protected endpoint rather than static files so auth is enforced.
app.MapGet("/dashboard", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.ContentRootPath, "Dashboard", "index.html"), "text/html"))
    .RequireAuthorization("Dashboard");

// Trigger Google sign-in and redirect back to dashboard on success
app.MapGet("/signin", () =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/dashboard" },
        [GoogleDefaults.AuthenticationScheme]));

// Sign out and return to landing page
app.MapPost("/signout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireAuthorization();

app.MapGet("/denied", () =>
    Results.Text("Access denied — this dashboard is private.", "text/plain", statusCode: 403));

// Apply pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
