namespace MoogleAPI.Web.Infrastructure.RateLimiting;

/// <summary>
/// Named for what these keys buy rather than what they are: the API itself needs no key, and
/// <c>ApiKeyOptions</c> would collide with Scalar's type of that name in <c>Program.cs</c>.
/// </summary>
public class PremiumKeyOptions
{
    public const string SectionName = "ApiKeys";

    /// <summary>
    /// The keys entitled to the premium rate limit. Anything not on this list is treated as
    /// anonymous, so an empty list simply means nobody has premium — which is the correct
    /// default, and the reason this isn't validated at startup the way
    /// <see cref="Puzzles.DailyPuzzleOptions.Secret"/> is.
    /// </summary>
    /// <remarks>
    /// Supply via <c>ApiKeys__Keys__0</c>, <c>ApiKeys__Keys__1</c>, … (env vars) or user-secrets
    /// in development. These are credentials: keep them out of appsettings.json.
    /// </remarks>
    public List<string> Keys { get; set; } = [];
}
