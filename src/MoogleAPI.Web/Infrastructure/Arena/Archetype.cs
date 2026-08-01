using System.Text.RegularExpressions;

namespace MoogleAPI.Web.Infrastructure.Arena;

/// <summary>
/// How a character fights, reduced to the four shapes the combat model can tell apart.
/// </summary>
/// <remarks>
/// Four rather than the series' dozens of jobs because the battle only has two offensive stats
/// and two defensive ones. A Dragoon and a Samurai differ in ways nothing here can express, so
/// splitting them would be a label with no mechanics behind it.
/// </remarks>
public enum Archetype
{
    /// <summary>No pronounced lean. What a character falls back to when nothing identifies them.</summary>
    Balanced,

    /// <summary>Physical damage and armour. Swords, axes, lances.</summary>
    Warrior,

    /// <summary>Magic damage and resistance, thin defence. Rods, staves, dolls.</summary>
    Mage,

    /// <summary>Speed over either kind of bulk. Daggers, knuckles, firearms.</summary>
    Scout,
}

/// <summary>
/// Works out a character's <see cref="Archetype"/> from what the wiki records about them.
/// </summary>
/// <remarks>
/// Three sources, tried in order of how directly each answers the question.
/// <list type="number">
/// <item><c>Job</c> is the wiki stating the class outright — "Black Mage", "Knight".</item>
/// <item><c>Weapon</c> is nearly as good and far more widely present: about half the roster has
/// no job, because the job-system games have no fixed class to record, but almost every article
/// lists an armament. Knuckles are a monk, staves a mage, firearms a marksman.</item>
/// <item><c>Abilities</c> catches the rest through the series' command names — "Blk Mag",
/// "Steal", "Wht Mag".</item>
/// </list>
/// <para>
/// <c>Role</c> is deliberately not consulted, though it is the most populated field of the four.
/// It holds the character's occupation, which in this series has almost nothing to do with how
/// they fight: Aerith's is "Florist" and Tifa's "Bar hostess". Reading it would classify the
/// party by day job.
/// </para>
/// </remarks>
public static class ArchetypeReader
{
    private static readonly (Archetype Archetype, Regex Pattern)[] JobPatterns =
    [
        (Archetype.Mage, new Regex(@"mage|wizard|sorcer|summoner|magus|necromancer|oracle|magick", RegexOptions.IgnoreCase)),
        (Archetype.Scout, new Regex(@"thief|ninja|rogue|bandit|pirate|ranger|hunter|gambler|marksman|monk|assassin", RegexOptions.IgnoreCase)),
        (Archetype.Warrior, new Regex(@"knight|warrior|soldier|dragoon|samurai|fighter|paladin|guardian|berserker|gladiator|templar", RegexOptions.IgnoreCase)),
    ];

    // Plurals are the rule rather than the exception in this field — articles write "Swords",
    // "Rods", "Knuckles" — and compounds are common too, so each stem takes an optional "s" and
    // the leading boundary is dropped where a word can be suffixed onto another ("Greatswords").
    private static readonly (Archetype Archetype, Regex Pattern)[] WeaponPatterns =
    [
        (Archetype.Mage, new Regex(@"\b(rods?|staff|staves|dolls?|tomes?|books?|grimoires?|bells?|instruments?)\b", RegexOptions.IgnoreCase)),
        (Archetype.Scout, new Regex(@"\b(knuckles?|claws?|daggers?|knife|knives|firearms?|guns?|pistols?|bows?|boomerangs?|throwing|whips?|rackets?|cards?|dice|coins?)\b", RegexOptions.IgnoreCase)),
        (Archetype.Warrior, new Regex(@"(swords?|katanas?|\baxes?\b|\bspears?\b|\blances?\b|\bhammers?\b|\bmaces?\b|\bshields?\b|blades?)\b", RegexOptions.IgnoreCase)),
    ];

    private static readonly (Archetype Archetype, Regex Pattern)[] AbilityPatterns =
    [
        (Archetype.Mage, new Regex(@"blk ?mag|wht ?mag|black magic|white magic|summon|eidolon|magic|espers?|blu ?mag|blue magic", RegexOptions.IgnoreCase)),
        (Archetype.Scout, new Regex(@"steal|mug|throw|jump|slots|dance|sing|gil toss|aim|flee|master thief", RegexOptions.IgnoreCase)),
        (Archetype.Warrior, new Regex(@"swd ?art|bushido|blitz|runic|cover|swordtech|dualcast|tools|defend|guard|lancet", RegexOptions.IgnoreCase)),
    ];

    public static Archetype For(string? job, string? weapon, string? abilities) =>
        Match(job, JobPatterns)
        ?? MatchInOrder(weapon, WeaponPatterns)
        ?? Match(abilities, AbilityPatterns)
        ?? Archetype.Balanced;

    private static Archetype? Match(string? value, (Archetype Archetype, Regex Pattern)[] patterns)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var (archetype, pattern) in patterns)
            if (pattern.IsMatch(value))
                return archetype;

        return null;
    }

    /// <summary>
    /// Matches a comma-separated list one entry at a time, so the article's own ordering decides
    /// rather than the order the patterns happen to be written in.
    /// </summary>
    /// <remarks>
    /// Weapon fields routinely list several. Terra's is "Most swords, daggers", and testing the
    /// whole string against each pattern in turn made her a Scout on the strength of the daggers
    /// she is listed as being able to hold — the article leads with swords because that is what
    /// she fights with. Reading it in order says Warrior, which is also what her Magitek Elite
    /// job would have said had the job patterns known the term.
    /// </remarks>
    private static Archetype? MatchInOrder(string? value, (Archetype Archetype, Regex Pattern)[] patterns)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var entry in value.Split([',', '/', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Match(entry, patterns);
            if (match is not null) return match;
        }

        return null;
    }

    /// <summary>
    /// What each archetype does to the baseline stats its level earns it.
    /// </summary>
    /// <remarks>
    /// Every archetype's multipliers cancel out: what a Mage gains in magic it gives back in
    /// physical, so no choice on the roster screen is simply the strongest one. They are also
    /// modest on purpose. <see cref="Battle.BattleMath.Ratio"/> is clamped to [0.2, 0.8], so a
    /// multiplier well past 1.5 would keep sliding the input without moving the result — a
    /// bigger number that changes nothing, which is worse than no number at all.
    /// </remarks>
    public static StatWeights WeightsFor(Archetype archetype) => archetype switch
    {
        // Each set sums to 6.00 — one whole stat's worth of weighting per stat, redistributed.
        // That is what keeps the roster a choice rather than a ranking.
        Archetype.Warrior => new StatWeights(HitPoints: 1.15, Attack: 1.25, Defense: 1.15, MagicAttack: 0.70, MagicDefense: 0.80, Speed: 0.95),
        Archetype.Mage => new StatWeights(HitPoints: 0.85, Attack: 0.70, Defense: 0.85, MagicAttack: 1.35, MagicDefense: 1.25, Speed: 1.00),
        Archetype.Scout => new StatWeights(HitPoints: 0.85, Attack: 1.10, Defense: 0.90, MagicAttack: 0.90, MagicDefense: 0.90, Speed: 1.35),
        _ => new StatWeights(1, 1, 1, 1, 1, 1),
    };
}

/// <param name="HitPoints">Scales the level's baseline HP.</param>
public record StatWeights(
    double HitPoints, double Attack, double Defense, double MagicAttack, double MagicDefense, double Speed);
