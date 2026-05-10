using System.Security.Cryptography;
using System.Text;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Web.Infrastructure.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only log API calls — skip static files, dashboard, docs
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var start = Environment.TickCount64;
        await next(context);
        var durationMs = (int)(Environment.TickCount64 - start);

        // Fire-and-forget: never slow down the response for logging
        _ = WriteLogAsync(context, path, durationMs);
    }

    private async Task WriteLogAsync(HttpContext context, string path, int durationMs)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.RequestLogs.Add(new RequestLog
            {
                Timestamp    = DateTime.UtcNow,
                Path         = path,
                Method       = context.Request.Method,
                StatusCode   = context.Response.StatusCode,
                DurationMs   = durationMs,
                ResourceType = ExtractResourceType(path),
                SearchTerm   = context.Request.Query["query"].FirstOrDefault(),
                IsPremium    = context.Request.Headers.ContainsKey("X-Api-Key"),
                IpHash       = HashIp(context.Connection.RemoteIpAddress?.ToString()),
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
