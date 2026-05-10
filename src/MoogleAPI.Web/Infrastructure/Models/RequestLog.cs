namespace MoogleAPI.Web.Infrastructure.Models;

public class RequestLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? ResourceType { get; set; }
    public string? SearchTerm { get; set; }
    public bool IsPremium { get; set; }
    public string? IpHash { get; set; }
}
