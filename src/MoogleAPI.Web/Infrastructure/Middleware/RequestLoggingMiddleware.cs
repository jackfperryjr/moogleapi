using System.Security.Cryptography;
using System.Text;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;
using MoogleAPI.Web.Infrastructure.RateLimiting;

namespace MoogleAPI.Web.Infrastructure.Middleware;

public class RequestLoggingMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopeFactory,
    ApiKeyValidator apiKeys)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only log API calls — skip static files, pages, docs
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // ...and skip the owner's own tooling. The dashboard polls its tables and the stats page
        // refreshes every two minutes, so logging them would leave /api/stats sitting at the top
        // of "Top Endpoints" — the analytics measuring the act of reading the analytics.
        if (path.StartsWith("/api/dashboard", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/stats", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var start = Environment.TickCount64;
        await next(context);
        var durationMs = (int)(Environment.TickCount64 - start);

        // Read off the request before handing to the background write — by the time that runs
        // the response is done and the context is no longer ours to read.
        var isPremium = apiKeys.ResolveKey(context.Request) is not null;

        // Fire-and-forget: never slow down the response for logging
        _ = WriteLogAsync(context, path, durationMs, isPremium);
    }

    private async Task WriteLogAsync(HttpContext context, string path, int durationMs, bool isPremium)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.RequestLogs.Add(new RequestLog
            {
                Timestamp = DateTime.UtcNow,
                Path = path,
                Method = context.Request.Method,
                StatusCode = context.Response.StatusCode,
                DurationMs = durationMs,
                ResourceType = ExtractResourceType(path),
                SearchTerm = context.Request.Query["query"].FirstOrDefault(),
                // Recognized key, not merely a present header — otherwise the premium share in
                // /api/stats counts anyone who sent the header, valid or not.
                IsPremium = isPremium,
                IpHash = HashIp(context.Connection.RemoteIpAddress?.ToString()),
            });

            await db.SaveChangesAsync();
        }
        catch
        {
            // Logging must never surface errors to the caller
        }
    }

    private static string? ExtractResourceType(string path)
    {
        // "/api/characters/search" → "characters"
        var parts = path.TrimStart('/').Split('/');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static string HashIp(string? ip)
    {
        if (ip is null) return "unknown";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(bytes)[..16];
    }
}
