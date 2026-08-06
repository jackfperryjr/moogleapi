using MoogleAPI.Web.Infrastructure.Models;
using MoogleAPI.Web.Infrastructure.RateLimiting;

namespace MoogleAPI.Web.Infrastructure.Middleware;

public class RequestLoggingMiddleware(
    RequestDelegate next,
    RequestLogWriter writer,
    ApiKeyValidator apiKeys,
    ClientIpResolver clientIps)
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

        writer.Write(new RequestLog
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
            // The caller, not the load balancer — see ClientIpResolver for why the peer address
            // was neither. Rows written before that fix hash Railway's internal pool instead, so
            // per-client figures are only meaningful from the deploy onward.
            IpHash = RequestLogWriter.HashIp(clientIps.Resolve(context.Request)),
        });
    }

    private static string? ExtractResourceType(string path)
    {
        // "/api/characters/search" → "characters"
        var parts = path.TrimStart('/').Split('/');
        return parts.Length >= 2 ? parts[1] : null;
    }
}
