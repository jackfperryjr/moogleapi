namespace MoogleAPI.Web.Infrastructure.Puzzles;

public class DailyPuzzleOptions
{
    public const string SectionName = "DailyPuzzle";

    /// <summary>
    /// Server-side key used to derive each day's puzzle seed. Must stay secret and must stay
    /// stable — rotating it reshuffles every past and future answer. Supply via
    /// <c>DailyPuzzle__Secret</c> (env var) or user-secrets in development.
    /// </summary>
    public string Secret { get; set; } = "";
}
