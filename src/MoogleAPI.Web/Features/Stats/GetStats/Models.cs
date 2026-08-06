namespace MoogleAPI.Web.Features.Stats.GetStats;

/// <summary>
/// Query for the dashboard. Both bounds are optional and UTC; the default is the last 24 hours,
/// which is what the page asked for before it could ask for anything else.
/// </summary>
public class StatsRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public record DashboardStats(
    RangeInfo Range,
    SummaryStats Summary,
    List<BucketCount> RequestsOverTime,
    List<StatusCount> StatusCodes,
    List<EndpointCount> TopEndpoints,
    List<SearchCount> TopSearchTerms,
    LatencyStats Latency,
    // Added 2026-08-05, all of them answering questions the 24-hour view couldn't.
    TrafficSplit Traffic,
    List<ErrorPathCount> TopErrorPaths,
    List<EndpointLatency> SlowestEndpoints,
    List<ClientCount> TopClients
);

/// <summary>
/// What was actually measured. Echoed back because the page lets you pick a range, and a chart
/// whose axis silently switched from hours to days is a chart that lies about its own shape.
/// </summary>
public record RangeInfo(DateTime From, DateTime To, string Granularity, bool Truncated);

public record SummaryStats(
    long TotalRequests,
    long RequestsInRange,
    long ErrorsInRange,
    long RateLimitedInRange,
    int UniqueClientsInRange
);

public record BucketCount(DateTime Bucket, int Count);
public record StatusCount(int StatusCode, int Count);
public record EndpointCount(string Path, int Count);
public record SearchCount(string Term, int Count, string Resource);
public record LatencyStats(double AvgMs, int P50Ms, int P95Ms);

/// <summary>Keyed vs anonymous traffic — the thing to watch if premium keys are ever sold.</summary>
public record TrafficSplit(int PremiumRequests, int AnonymousRequests);

/// <summary>Which paths are failing, and how.</summary>
public record ErrorPathCount(string Path, int StatusCode, int Count);

/// <summary>
/// Per-endpoint p95: the list form of the single latency figure, so one slow endpoint can't hide
/// inside a healthy average.
/// </summary>
public record EndpointLatency(string Path, int P95Ms, int Count);

/// <summary>Busiest callers by hashed address. See the endpoint for the caveat on older rows.</summary>
public record ClientCount(string IpHash, int Count, bool IsPremium);
