using System.Text.RegularExpressions;

namespace MoogleAPI.Web.Infrastructure.Wiki;

/// <summary>
/// Hygiene for values that came off a wiki page.
/// </summary>
/// <remarks>
/// These were written to repair damage the bulk scraper had already done to the database, and
/// they outlived it because the damage was never really about the bulk run: it is what wikitext
/// does when a parse goes half-right. Importing one page at a time hits the same cases — a title
/// whose disambiguator was stripped out of its middle, a field that captured the next infobox
/// assignment instead of a value — with the difference that nothing is written until you have
/// looked at it. Cleaning here means the import preview shows a name, not "Lamia  IV".
/// </remarks>
public static partial class WikiText
{
    /// <summary>
    /// Drops the disambiguating parenthetical a wiki title carries when the name alone is
    /// ambiguous: "Bomb (Final Fantasy II)" → "Bomb", "Auron (Final Fantasy X party member)"
    /// → "Auron".
    /// </summary>
    /// <remarks>
    /// The parenthetical belongs to the wiki's filing system, not to the character. Every row in
    /// the catalogue is stored under the plain name — the game it belongs to is a column — so an
    /// import that kept the suffix would read as a different creature from the one already there
    /// and would never be recognised as a duplicate of it.
    /// </remarks>
    public static string NormalizeName(string title) =>
        RepeatedWhitespace().Replace(TrailingParenthetical().Replace(title, ""), " ").Trim();

    /// <summary>"Noctis  XV party member" → "Noctis". Idempotent on already-clean names.</summary>
    public static string RepairName(string name) =>
        RepeatedWhitespace().Replace(GameNumeralSuffix().Replace(name, ""), " ").Trim();

    /// <summary>"Lamia  IV" → "Lamia", "Emperor (final boss" → "Emperor". Idempotent.</summary>
    public static string RepairMonsterName(string name)
    {
        name = UnclosedParenthetical().Replace(name, "");
        name = GameNumeralSuffix().Replace(name, "");
        name = DisambiguatorResidue().Replace(name, "");
        return RepeatedWhitespace().Replace(name, " ").Trim();
    }

    /// <summary>
    /// Nulls a value that still carries wikitext rather than storing the markup as if it were
    /// content. A field holding "|race=Android" is not a race, it is a failed parse.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (UnparsedInfobox().IsMatch(value)) return null;
        if (value.StartsWith('|') || value.Contains("{{") || value.Contains("[[")) return null;
        return value.Trim();
    }

    /// <summary>True for reference and index pages that describe enemies rather than being one.</summary>
    public static bool IsNotAMonster(string name) => NotAMonster().IsMatch(name);

    // Two-or-more spaces followed by a Final Fantasy numeral and anything after it.
    [GeneratedRegex(@"\s{2,}(?:IX|IV|XI{0,3}|XIV|XV|XVI|VI{0,3}|V|I{1,3}|X)\b.*$")]
    private static partial Regex GameNumeralSuffix();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedWhitespace();

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex TrailingParenthetical();

    // A value that still carries wikitext template syntax was never parsed properly.
    // Field names may contain spaces ("|japanese voice actor ="), hence the space in the class.
    [GeneratedRegex(@"\|\s*[a-zA-Z_][a-zA-Z_ ]*\s*=")]
    private static partial Regex UnparsedInfobox();

    // An unclosed parenthetical is the same damage seen from the other side: the closing
    // half of "Borghen (Final Fantasy II boss)" went with the stripped game title.
    [GeneratedRegex(@"\s*\([^)]*$")]
    private static partial Regex UnclosedParenthetical();

    // What's left of a disambiguator once the game title is gone: "Chaos  boss", "Bomb  creature".
    [GeneratedRegex(@"\s{2,}(boss|enemy|creature|ability|type|character|party member)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DisambiguatorResidue();

    [GeneratedRegex(
        @"^enem(y|ies)$" +
        @"|\b(enem(y|ies)?\s*\(?\s*(abilit(y|ies)|types?|famil(y|ies)|actions?|formations|stats|data)|enemies|bestiary|list of)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotAMonster();
}
