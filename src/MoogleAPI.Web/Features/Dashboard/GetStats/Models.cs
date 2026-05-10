namespace MoogleAPI.Web.Features.Dashboard.GetStats;

public record DashboardStats(
    SummaryStats         Summary,
    List<HourlyCount>    RequestsOverTime,
    List<StatusCount>    StatusCodes,
    List<EndpointCount>  TopEndpoints,
    List<SearchCount>    TopSearchTerms,
    LatencyStats         Latency
);

public record SummaryStats(
    long TotalRequests,
    long RequestsToday,
    long ErrorsToday,
    long RateLimitedToday,
    int  UniqueIpsToday
);

public record HourlyCount(DateTime Hour, int Count);
public record StatusCount(int StatusCode, int Count);
public record EndpointCount(string Path, int Count);
public record SearchCount(string Term, int Count, string Resource);
public record LatencyStats(double AvgMs, int P50Ms, int P95Ms);
