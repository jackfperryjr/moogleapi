using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Stats.GetStats;

public class Endpoint(AppDbContext db) : Endpoint<StatsRequest, DashboardStats>
{
    /// <summary>
    /// Past this many rows the range is trimmed to its most recent slice rather than loaded whole.
    /// The aggregation runs in memory — at three months and ~7,800 rows that is the right trade for
    /// a one-reader dashboard, but "all time" grows without bound, and this keeps a future year of
    /// traffic from turning one page load into an out-of-memory restart.
    /// </summary>
    private const int MaxRows = 250_000;

    /// <summary>Hourly buckets stop being readable somewhere past a few days.</summary>
    private static readonly TimeSpan HourlyLimit = TimeSpan.FromDays(3);

    public override void Configure()
    {
        // Global RoutePrefix "api" is prepended, so the final URL is /api/stats
        Get("/stats");
        Policies("Dashboard");
        // Owner-only, so it has no business in the public reference
        Description(b => b.ExcludeFromDescription());
    }

    public override async Task HandleAsync(StatsRequest req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Bounds are clamped rather than rejected: this is a personal dashboard driven by two date
        // boxes, and a 400 for from-after-to would be pedantry rather than safety.
        var to = req.To is null ? now : AsUtc(req.To.Value);
        var from = req.From is null ? to.AddHours(-24) : AsUtc(req.From.Value);

        // Order first, then clamp. Clamping before the swap lets a range that is entirely in the
        // future come back out of it — the pair would be reordered around the clamped end and the
        // page would report measuring up to a date months away.
        if (from > to)
            (from, to) = (to, from);
        if (to > now)
            to = now;
        if (from > to)
            from = to;

        var totalRequests = await db.RequestLogs.LongCountAsync(ct);

        var logs = await db.RequestLogs
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .OrderByDescending(r => r.Timestamp)
            .Select(r => new
            {
                r.Timestamp,
                r.StatusCode,
                r.DurationMs,
                r.Path,
                r.SearchTerm,
                r.ResourceType,
                r.IpHash,
                r.IsPremium,
            })
            .Take(MaxRows + 1)
            .ToListAsync(ct);

        var truncated = logs.Count > MaxRows;
        if (truncated)
        {
            logs = logs.Take(MaxRows).ToList();
            // Report the window actually covered, not the one that was asked for.
            from = logs[^1].Timestamp;
        }

        var useHours = to - from <= HourlyLimit;
        var range = new RangeInfo(from, to, useHours ? "hour" : "day", truncated);

        var summary = new SummaryStats(
            TotalRequests: totalRequests,
            RequestsInRange: logs.Count,
            ErrorsInRange: logs.Count(r => r.StatusCode >= 400),
            // Only populated for rows written from 2026-08-05 on: before that, rejections
            // short-circuited ahead of the logging middleware and were never recorded at all.
            RateLimitedInRange: logs.Count(r => r.StatusCode == 429),
            UniqueClientsInRange: logs.Where(r => r.IpHash != null).Select(r => r.IpHash).Distinct().Count()
        );

        var requestsOverTime = logs
            .GroupBy(r => useHours
                ? new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, 0, 0, DateTimeKind.Utc)
                : new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, 0, 0, 0, DateTimeKind.Utc))
            .Select(g => new BucketCount(g.Key, g.Count()))
            .OrderBy(b => b.Bucket)
            .ToList();

        var statusCodes = logs
            .GroupBy(r => r.StatusCode)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList();

        var topEndpoints = logs
            .GroupBy(r => r.Path)
            .Select(g => new EndpointCount(g.Key, g.Count()))
            .OrderByDescending(e => e.Count)
            .Take(10)
            .ToList();

        var topSearchTerms = logs
            .Where(r => !string.IsNullOrWhiteSpace(r.SearchTerm))
            .GroupBy(r => new { Term = r.SearchTerm!, r.ResourceType })
            .Select(g => new SearchCount(g.Key.Term, g.Count(), g.Key.ResourceType ?? "api"))
            .OrderByDescending(s => s.Count)
            .Take(20)
            .ToList();

        var durations = logs.Select(r => r.DurationMs).OrderBy(d => d).ToList();
        var latency = durations.Count > 0
            ? new LatencyStats(Math.Round(durations.Average(), 1), Percentile(durations, 0.50), Percentile(durations, 0.95))
            : new LatencyStats(0, 0, 0);

        var traffic = new TrafficSplit(
            PremiumRequests: logs.Count(r => r.IsPremium),
            AnonymousRequests: logs.Count(r => !r.IsPremium));

        var topErrorPaths = logs
            .Where(r => r.StatusCode >= 400)
            .GroupBy(r => new { r.Path, r.StatusCode })
            .Select(g => new ErrorPathCount(g.Key.Path, g.Key.StatusCode, g.Count()))
            .OrderByDescending(e => e.Count)
            .Take(10)
            .ToList();

        // A p95 over two requests is not a p95. Endpoints below the floor are left out rather than
        // shown with a number that would read as authoritative.
        const int minSamplesForPercentile = 5;
        var slowestEndpoints = logs
            .GroupBy(r => r.Path)
            .Where(g => g.Count() >= minSamplesForPercentile)
            .Select(g => new EndpointLatency(
                g.Key,
                Percentile([.. g.Select(r => r.DurationMs).OrderBy(d => d)], 0.95),
                g.Count()))
            .OrderByDescending(e => e.P95Ms)
            .Take(10)
            .ToList();

        // Hashed addresses, and only honest for rows written after the client-IP fix shipped on
        // 2026-08-05. Everything before it hashed Railway's internal load balancer, so historic
        // "clients" are a pool of about twenty addresses belonging to the host, not to callers.
        var topClients = logs
            .Where(r => r.IpHash != null)
            .GroupBy(r => r.IpHash!)
            .Select(g => new ClientCount(g.Key, g.Count(), g.Any(r => r.IsPremium)))
            .OrderByDescending(c => c.Count)
            .Take(10)
            .ToList();

        await Send.OkAsync(new DashboardStats(
            range, summary, requestsOverTime, statusCodes, topEndpoints, topSearchTerms, latency,
            traffic, topErrorPaths, slowestEndpoints, topClients), ct);
    }

    /// <summary>
    /// Forces a bound to UTC before it reaches the query.
    /// </summary>
    /// <remarks>
    /// Model binding hands back <c>Kind=Local</c> for an ISO string ending in <c>Z</c>, and Npgsql
    /// refuses to write anything but UTC to a <c>timestamp with time zone</c> column — so every
    /// range the page could ask for answered 500 while the default 24 hours, built from
    /// <see cref="DateTime.UtcNow"/>, worked fine. A value with no zone at all is read as UTC
    /// rather than converted, because the dashboard states its ranges in UTC throughout.
    /// </remarks>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Nearest-rank percentile over an already-sorted list. Extracted because the per-endpoint p95
    /// needs the same calculation the overall one does, and two copies of a percentile drift.
    /// </summary>
    private static int Percentile(List<int> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
