using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MoogleAPI.Web.Infrastructure.RateLimiting;

public static class ApiRateLimiterPolicy
{
    public const string Anonymous = "anonymous";
    public const string Premium = "premium";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Anonymous, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Premium users identified by X-Api-Key header get 10x the limit
            options.AddPolicy(Premium, context =>
            {
                var apiKey = context.Request.Headers["X-Api-Key"].ToString();
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: string.IsNullOrEmpty(apiKey) ? $"ip:{context.Connection.RemoteIpAddress}" : $"key:{apiKey}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = string.IsNullOrEmpty(apiKey) ? 60 : 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var apiKey = context.Request.Headers["X-Api-Key"].ToString();
                var limit = string.IsNullOrEmpty(apiKey) ? 60 : 600;
                var key = string.IsNullOrEmpty(apiKey)
                    ? $"ip:{context.Connection.RemoteIpAddress}"
                    : $"key:{apiKey}";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1)
                });
            });
        });

        return services;
    }
}
