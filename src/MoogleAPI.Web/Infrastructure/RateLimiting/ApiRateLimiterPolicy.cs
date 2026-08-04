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

                var partitionKey = apiKey is null
                    ? $"ip:{context.Connection.RemoteIpAddress}"
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
