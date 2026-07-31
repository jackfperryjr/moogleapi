namespace MoogleAPI.Web.Infrastructure.Battle;

/// <summary>
/// A tiny SplitMix64 generator, used instead of <see cref="Random"/> because a gauntlet has to
/// rebuild identically on every request. <c>Random(int)</c> would tie the run to a 32-bit seed
/// and, worse, to whatever shuffling algorithm the runtime ships that year — a framework
/// upgrade could silently reshuffle every past day's opponents. This algorithm is fixed here.
/// </summary>
public sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed;

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform value in [0, exclusiveUpperBound).</summary>
    public int Next(int exclusiveUpperBound) =>
        exclusiveUpperBound <= 0 ? 0 : (int)(NextUInt64() % (ulong)exclusiveUpperBound);

    /// <summary>
    /// Derives an independent stream from this one. Each rung draws from its own stream so
    /// that adding a battle to rung 3 can't shift which opponents rung 4 picks.
    /// </summary>
    public static DeterministicRandom ForScope(ulong seed, params object[] scope)
    {
        var mixed = seed;
        foreach (var part in scope)
        {
            mixed ^= (ulong)(part.ToString() ?? "").GetDeterministicHash();
            mixed *= 0x100000001B3UL;
        }

        return new DeterministicRandom(mixed);
    }
}

internal static class StringHashExtensions
{
    /// <summary>
    /// FNV-1a. <see cref="string.GetHashCode()"/> is randomized per process, so using it would
    /// give a different gauntlet on every app restart.
    /// </summary>
    public static long GetDeterministicHash(this string value)
    {
        unchecked
        {
            var hash = 0xCBF29CE484222325UL;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 0x100000001B3UL;
            }

            return (long)hash;
        }
    }
}
