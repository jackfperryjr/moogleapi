using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// The series' eight elements. Absent — a null <see cref="Element"/> — is non-elemental, which is
/// what a plain physical swing is and what every unrecognised ability name falls back to.
/// </summary>
public enum Element { Fire, Ice, Thunder, Water, Earth, Wind, Holy, Dark }

/// <summary>
/// What an element does to another one, and how a monster comes to have one at all.
/// </summary>
/// <remarks>
/// Two sources feed this and they are not equals. The wiki publishes real per-monster
/// <c>Weaknesses</c> and <c>Absorbs</c>, and that data always wins: it is the actual game's answer
/// for that actual monster. The grid below only fills the silence — the great majority of
/// element-versus-monster pairings the articles say nothing about — so that there is a
/// "not very effective" tier at all, which the wiki data alone cannot provide.
/// </remarks>
public static class Elements
{
    /// <summary>
    /// The series' opposed pairs. Fire and Ice, Thunder and Water, Earth and Wind, Holy and Dark
    /// are paired in every game that has an element system at all.
    /// </summary>
    private static readonly Dictionary<Element, Element> Opposites = new()
    {
        [Element.Fire] = Element.Ice,
        [Element.Ice] = Element.Fire,
        [Element.Thunder] = Element.Water,
        [Element.Water] = Element.Thunder,
        [Element.Earth] = Element.Wind,
        [Element.Wind] = Element.Earth,
        [Element.Holy] = Element.Dark,
        [Element.Dark] = Element.Holy,
    };

    public static Element? Opposite(Element element) =>
        Opposites.TryGetValue(element, out var opposite) ? opposite : null;

    public const double SuperEffective = 2.0;
    public const double NotVeryEffective = 0.5;
    public const double Neutral = 1.0;

    /// <summary>Absorbing turns damage into nothing. Healing off it is a rule the client applies.</summary>
    public const double Absorbed = 0.0;

    /// <summary>
    /// The grid: what <paramref name="attack"/> is worth against a monster whose own element is
    /// <paramref name="defender"/>.
    /// </summary>
    /// <remarks>
    /// Symmetric, unlike Pokémon's chart — Fire beats Ice <em>and</em> Ice beats Fire. That is
    /// deliberate. Final Fantasy's pairs are oppositions rather than a directed cycle, and a
    /// one-way chart here would be inventing a hierarchy the series does not have; the player
    /// would have to memorise it, where "opposites hurt each other" needs no instruction at all.
    /// <para>
    /// An element is resisted by itself, which is the one asymmetry worth keeping: it stops a
    /// Fire party bulldozing a Fire hunt with Fire, and it is what the series does whenever a
    /// creature is made of the thing you are throwing at it.
    /// </para>
    /// </remarks>
    public static double Effectiveness(Element attack, Element? defender)
    {
        if (defender is null) return Neutral;
        if (attack == defender) return NotVeryEffective;

        return Opposite(attack) == defender ? SuperEffective : Neutral;
    }

    /// <summary>
    /// The element a monster <em>is</em>, which decides both the bonus on its own moves and what
    /// the grid does to it.
    /// </summary>
    /// <remarks>
    /// Nothing publishes this. Final Fantasy has no "type" field on an enemy — it has a list of
    /// things that hurt it and a list of things it drinks — so the monster's own element has to be
    /// read off the evidence, in confidence order:
    /// <list type="number">
    /// <item>
    /// <b>What it absorbs.</b> The strongest signal by far and almost never ambiguous. A Bomb
    /// absorbs Fire because a Bomb <em>is</em> fire; nothing else absorbs an element it is not
    /// made of.
    /// </item>
    /// <item>
    /// <b>What it casts.</b> The plurality element across its ability list. A creature throwing
    /// three Blizzard variants is an Ice creature, whatever else it does.
    /// </item>
    /// <item>
    /// <b>The inverse of a lone weakness.</b> Weak to Ice and nothing else is decent evidence of
    /// being Fire. Only used when the weakness is unambiguous, because two weaknesses point at
    /// two different elements and neither is more right.
    /// </item>
    /// </list>
    /// Falling through all three leaves it non-elemental, which is the safe answer: it forfeits
    /// the affinity bonus and takes neutral damage from everything, so a bad guess costs the
    /// player nothing they can notice.
    /// </remarks>
    public static Element? Affinity(string? absorbs, string? abilities, string? weaknesses)
    {
        // Cast before FirstOrDefault. Element is a value type, so the default of an empty sequence
        // is Element.Fire rather than nothing — which silently made every monster with no listed
        // absorptions a Fire monster, and that is most of the library.
        if (Parse(Split(absorbs)).Cast<Element?>().FirstOrDefault() is { } absorbed) return absorbed;

        if (CastingPlurality(abilities) is { } cast) return cast;

        var weak = Parse(Split(weaknesses)).Distinct().ToList();
        return weak.Count == 1 ? Opposite(weak[0]) : null;
    }

    /// <summary>
    /// The element the monster throws most often, or null when it throws none or splits evenly.
    /// </summary>
    /// <remarks>
    /// A tie is treated as no answer rather than resolved by order. A creature with one Fire spell
    /// and one Ice spell is a mage, not a Fire monster, and picking whichever the article happened
    /// to list first would hand out affinity bonuses on the strength of an editor's ordering.
    /// </remarks>
    private static Element? CastingPlurality(string? abilities)
    {
        if (string.IsNullOrWhiteSpace(abilities)) return null;

        var counts = new Dictionary<Element, int>();
        foreach (var ability in Split(abilities))
        {
            if (MoveBuilder.ElementFor(ability) is not { } name) continue;
            if (!TryParse(name, out var element)) continue;

            counts[element] = counts.GetValueOrDefault(element) + 1;
        }

        if (counts.Count == 0) return null;

        var best = counts.MaxBy(pair => pair.Value);
        return counts.Count(pair => pair.Value == best.Value) == 1 ? best.Key : null;
    }

    /// <summary>
    /// Parses the wiki's element names. Unrecognised entries are dropped rather than guessed at:
    /// the affinity lists carry plenty that are not elements — "Gravity", "Instant Death",
    /// "Physical" — and forcing those onto the nearest element would invent resistances.
    /// </summary>
    public static bool TryParse(string? value, out Element element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();

        // The series writes Thunder as Lightning about as often, and both mean the same element.
        if (trimmed.Equals("Lightning", StringComparison.OrdinalIgnoreCase))
        {
            element = Element.Thunder;
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out element)
            && Enum.IsDefined(element);
    }

    public static IEnumerable<Element> Parse(IEnumerable<string> values)
    {
        foreach (var value in values)
            if (TryParse(value, out var element))
                yield return element;
    }

    public static IReadOnlyList<string> Split(string? commaSeparated) =>
        string.IsNullOrWhiteSpace(commaSeparated)
            ? []
            : commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
