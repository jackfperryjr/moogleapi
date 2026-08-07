namespace MoogleAPI.Scraper;

/// <summary>
/// An explicit list of rows a stage should confine itself to, parsed from <c>--ids=</c>.
/// </summary>
/// <remarks>
/// Both destructive stages used to be all-or-nothing: <c>unpromote</c> reverted every row holding
/// generated art, and <c>generate</c> could only be aimed with <c>--kinds</c>. That is too blunt
/// once the library is mostly right and the work is a named handful — withdrawing 250 bad images
/// meant either reverting all 5,569 or editing the database by hand.
/// <para>
/// Rows are named folder-first, <c>m</c> for a monster and <c>c</c> for a character, because an id
/// on its own is ambiguous: the two tables number from 1 independently, so <c>25</c> is both Sarah
/// and a monster. <c>--ids=m31,m52,c471</c>. A long list is unwieldy on a command line, so
/// <c>--ids=@file</c> reads the same syntax from a file, one entry per line or comma-separated,
/// with <c>#</c> comments.
/// </para>
/// </remarks>
public sealed class IdSelection
{
    private IdSelection(HashSet<int> monsters, HashSet<int> characters)
    {
        Monsters = monsters;
        Characters = characters;
    }

    public HashSet<int> Monsters { get; }

    public HashSet<int> Characters { get; }

    public int Count => Monsters.Count + Characters.Count;

    /// <summary>
    /// Reads a <c>--ids=</c> value, or null when the flag was absent — which every stage reads as
    /// "no restriction", the behaviour it had before this existed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value was given but names nothing usable. Silently treating a typo as "no restriction"
    /// would turn a mistake in a 250-row list into a run against the whole library.
    /// </exception>
    public static IdSelection? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.StartsWith('@')
            ? File.ReadAllText(value[1..].Trim())
            : value;

        var monsters = new HashSet<int>();
        var characters = new HashSet<int>();
        var bad = new List<string>();

        // Comments are stripped a line at a time, before anything is split on whitespace. Doing it
        // the other way round only drops the first word of a comment and then tries to read the
        // rest of the sentence as row ids.
        var tokens = text
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('#', 2)[0])
            .SelectMany(line => line.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            var set = char.ToLowerInvariant(token[0]) switch
            {
                'm' => monsters,
                'c' => characters,
                _ => null,
            };

            if (set is null || !int.TryParse(token[1..], out var id) || id <= 0)
            {
                bad.Add(token);
                continue;
            }

            set.Add(id);
        }

        if (bad.Count > 0)
        {
            throw new ArgumentException(
                $"--ids could not read {bad.Count} entr{(bad.Count == 1 ? "y" : "ies")}: "
                + $"{string.Join(", ", bad.Take(5))}{(bad.Count > 5 ? ", ..." : "")}. "
                + "Each one is m<id> for a monster or c<id> for a character, as in m31,c471.");
        }

        if (monsters.Count == 0 && characters.Count == 0)
        {
            throw new ArgumentException("--ids was given but named no rows.");
        }

        return new IdSelection(monsters, characters);
    }

    public override string ToString() => $"{Monsters.Count} monsters, {Characters.Count} characters";
}
