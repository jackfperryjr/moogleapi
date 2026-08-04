using Microsoft.Extensions.Options;

namespace MoogleAPI.Web.Infrastructure.RateLimiting;

/// <summary>
/// Decides whether an <c>X-Api-Key</c> header actually entitles a caller to the premium rate
/// limit. The header used to be taken at face value — any non-empty value bought 10× the
/// anonymous limit, so the limit was opt-out rather than enforced.
/// </summary>
public class ApiKeyValidator
{
    public const string HeaderName = "X-Api-Key";

    private readonly HashSet<string> _keys;

    public ApiKeyValidator(IOptions<PremiumKeyOptions> options) =>
        _keys = options.Value.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.Ordinal);

    public bool IsValid(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey) && _keys.Contains(apiKey.Trim());

    /// <summary>
    /// The recognized key on this request, or <c>null</c> for anonymous. An unrecognized key
    /// returns <c>null</c> rather than throwing: the API is public and documented as needing no
    /// key at all, so a bad one degrades to the anonymous limit instead of failing the request.
    /// </summary>
    public string? ResolveKey(HttpRequest request)
    {
        var apiKey = request.Headers[HeaderName].ToString();
        return IsValid(apiKey) ? apiKey.Trim() : null;
    }
}
