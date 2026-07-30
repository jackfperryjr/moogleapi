using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MoogleAPI.Web.Infrastructure.Puzzles;

/// <summary>
/// Derives the seed behind a daily puzzle answer.
/// </summary>
/// <remarks>
/// The seed is an HMAC over the puzzle date keyed by a server-held secret, rather than the
/// date itself. That distinction is the whole point: a plain date hash is reproducible by
/// anyone holding the character list, so players could compute every future answer offline.
/// Without the key they cannot, even knowing the date and the algorithm.
/// </remarks>
public class DailyPuzzle(IOptions<DailyPuzzleOptions> options)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.Secret);

    /// <summary>Today in UTC — the newest puzzle any caller is allowed to request.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static bool IsInFuture(DateOnly date) => date > Today;

    /// <param name="scope">
    /// Distinguishes puzzle families ("characters", "cards") so different games don't
    /// land on correlated answers for the same day.
    /// </param>
    public ulong SeedFor(DateOnly date, string scope)
    {
        var payload = Encoding.UTF8.GetBytes($"{scope}:{date:yyyy-MM-dd}");
        var hash = HMACSHA256.HashData(_key, payload);
        return BitConverter.ToUInt64(hash, 0);
    }
}
