using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;
using System.Security.Cryptography;
using System.Text;

namespace MoogleAPI.Web.Infrastructure.Middleware;

/// <summary>
/// Writes request-log rows, in the background, from the two places requests end.
/// </summary>
/// <remarks>
/// This exists because rate-limit rejections never reached <see cref="RequestLoggingMiddleware"/>.
/// <c>UseRateLimiter</c> sits ahead of it in the pipeline and short-circuits, so a 429 was answered
/// without anything being recorded — which is why three months of logs contain zero of them, and
/// why that zero was never evidence the limiter wasn't firing. The limiter's <c>OnRejected</c>
/// callback now writes through here instead, and reordering the pipeline was rejected as the fix:
/// moving <c>UseRateLimiter</c> after the static-file middleware would quietly exempt every script
/// and stylesheet from rate limiting.
/// </remarks>
public class RequestLogWriter(IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// Queues a row and returns immediately. Nothing here is allowed to slow down or fail a
    /// response — a dropped analytics row is worth less than the request it would have described.
    /// </summary>
    public void Write(RequestLog entry) => _ = WriteAsync(entry);

    private async Task WriteAsync(RequestLog entry)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RequestLogs.Add(entry);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Logging must never surface errors to the caller.
        }
    }

    /// <summary>
    /// Hashes the caller's address rather than storing it. Truncated to 16 hex characters, which
    /// is what the existing rows use — changing the width would split every client's history in
    /// two at the deploy.
    /// </summary>
    public static string HashIp(string? ip)
    {
        if (ip is null) return "unknown";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexString(bytes)[..16];
    }
}
