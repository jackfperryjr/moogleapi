using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.GetStats;

public class Endpoint(AppDbContext db) : EndpointWithoutRequest<DashboardStats>
{
    public override void Configure()
    {
        // Global RoutePrefix "api" is prepended, so the final URL is /api/dashboard/stats
        Get("/dashboard/stats");
        Policies("Dashboard");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now       = DateTime.UtcNow;
        var todayUtc  = now.Date;
        var last24h   = now.AddHours(-24);

        var totalRequests = await db.RequestLogs.LongCountAsync(ct);

        // Load last 24h into memory — small enough for a personal dashboard
        var logs = await db.RequestLogs
            .Where(r => r.Timestamp >= last24h)
            .Select(r => new
            {
                r.Timestamp,
                r.StatusCode,
                r.DurationMs,
                r.Path,
                r.SearchTerm,
                r.ResourceType,
                r.IpHash,
            })
            .ToListAsync(ct);

        var today = logs.Where(r => r.Timestamp >= todayUtc).ToList();

        var summary = new SummaryStats(
            TotalRequests:    totalRequests,
            RequestsToday:    today.Count,
            ErrorsToday:      today.Count(r => r.StatusCode >= 400),
            RateLimitedToday: today.Count(r => r.StatusCode == 429),
            UniqueIpsToday:   today.Where(r => r.IpHash != null).Select(r => r.IpHash).Distinct().Count()
        );

        var requestsOverTime = logs
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
            .Select(g => new HourlyCount(g.Key, g.Count()))
            .OrderBy(h => h.Hour)
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
            ? new LatencyStats(
                Math.Round(durations.Average(), 1),
                durations[durations.Count / 2],
                durations[(int)(durations.Count * 0.95)])
            : new LatencyStats(0, 0, 0);

        await Send.OkAsync(new DashboardStats(summary, requestsOverTime, statusCodes, topEndpoints, topSearchTerms, latency), ct);
    }
}
