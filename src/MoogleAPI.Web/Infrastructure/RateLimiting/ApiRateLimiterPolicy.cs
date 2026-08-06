using MoogleAPI.Web.Infrastructure.Middleware;
using MoogleAPI.Web.Infrastructure.Models;
using System.Threading.RateLimiting;

namespace MoogleAPI.Web.Infrastructure.RateLimiting;

public static class ApiRateLimiterPolicy
{
    public const int AnonymousPermitLimit = 60;
    public const int PremiumPermitLimit = 600;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Rejections short-circuit the pipeline, so the logging middleware downstream never
            // sees them. Without this the one status code the limiter exists to produce was the
            // one status code the analytics could not show.
            options.OnRejected = (context, _) =>
            {
                var path = context.HttpContext.Request.Path.Value ?? "";
                if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    return ValueTask.CompletedTask;

                var services = context.HttpContext.RequestServices;
                var clientIps = services.GetRequiredService<ClientIpResolver>();
                var apiKeys = services.GetRequiredService<ApiKeyValidator>();

                services.GetRequiredService<RequestLogWriter>().Write(new RequestLog
                {
                    Timestamp = DateTime.UtcNow,
                    Path = path,
                    Method = context.HttpContext.Request.Method,
                    StatusCode = StatusCodes.Status429TooManyRequests,
                    // The request was refused before any work happened; recording a measured
                    // duration here would drag the latency percentiles toward zero.
                    DurationMs = 0,
                    ResourceType = path.TrimStart('/').Split('/') is { Length: >= 2 } parts ? parts[1] : null,
                    SearchTerm = context.HttpContext.Request.Query["query"].FirstOrDefault(),
                    IsPremium = apiKeys.ResolveKey(context.HttpContext.Request) is not null,
                    IpHash = RequestLogWriter.HashIp(clientIps.Resolve(context.HttpContext.Request)),
                });

                return ValueTask.CompletedTask;
            };

            // One global limiter rather than named policies. There were two named policies here
            // that no endpoint ever attached with RequireRateLimiting, so they enforced nothing
            // while appearing to — and the premium one carried the same unchecked-key flaw as
            // this limiter did. Anything that needs a per-endpoint limit should be added back
            // deliberately, and wired up.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // The key is validated against the configured allowlist, so the partition is
                // only ever keyed on a credential we issued. Partitioning on the raw header
                // would otherwise let a caller mint unlimited fresh windows just by varying it.
                var validator = context.RequestServices.GetRequiredService<ApiKeyValidator>();
                var apiKey = validator.ResolveKey(context.Request);

                // Not Connection.RemoteIpAddress: behind Railway's load balancer that is one of
                // about twenty internal 100.64.x addresses shared by every caller, which made the
                // anonymous limit a pool nobody owned rather than a limit anybody hit. See
                // ClientIpResolver — and note it only trusts a forwarded address that arrives with
                // the edge secret, because a spoofable partition key is worse than a coarse one.
                var clientIps = context.RequestServices.GetRequiredService<ClientIpResolver>();

                var partitionKey = apiKey is null
                    ? $"ip:{clientIps.Resolve(context.Request)}"
                    : $"key:{apiKey}";

                var permitLimit = apiKey is null ? AnonymousPermitLimit : PremiumPermitLimit;

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }
}
