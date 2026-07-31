using System.Text.RegularExpressions;

namespace MoogleAPI.Web.Infrastructure.Battle;

/// <param name="Element">Fire, Ice, Thunder, Water, Earth, Wind, Holy, Dark — null is non-elemental.</param>
/// <param name="Kind">Physical moves are resisted by Defense, Magic moves by MagicDefense.</param>
/// <param name="Power">Damage multiplier applied to the attacker's relevant offence stat.</param>
/// <param name="Recoil">
/// Fraction of the user's own maximum HP the move costs. Non-zero only for the series'
/// self-destruct moves, which are the whole personality of a Bomb.
/// </param>
public record Move(string Name, string? Element, MoveKind Kind, double Power, double Recoil = 0);

public enum MoveKind { Physical, Magic }

/// <summary>
/// Turns a monster's scraped ability list into moves a player can actually choose between.
/// </summary>
/// <remarks>
/// The wiki gives ability <em>names</em> but no element or power — those live on each game's
/// separate "enemy abilities" article. Rather than scrape 16 more pages per game, the element
/// is inferred from the name, which works because Final Fantasy is relentlessly consistent
/// about its spell naming: anything Fire-elemental is some inflection of Fire, Blaze, or Flame
/// across all sixteen games. Names that match nothing stay non-elemental, which is the safe
/// default — it makes the move ordinary rather than wrongly strong against something.
/// </remarks>
public static class MoveBuilder
{
    private static readonly (string Element, string Pattern)[] ElementPatterns =
    [
        ("Fire",    @"fire|fira|firaga|flame|blaze|burn|inferno|ifrit|salamand|heat"),
        ("Ice",     @"ice|blizzard|blizzara|blizzaga|freez|frost|cold|glaci|shiva"),
        ("Thunder", @"thunder|thundara|thundaga|bolt|lightning|spark|shock|zap|ramuh|electro"),
        ("Water",   @"water|aqua|tsunami|tidal|splash|maelstrom|leviathan"),
        ("Earth",   @"earth|quake|tremor|stone|rock|sand|titan|landslide"),
        // "aer[oa]" covers the whole spell line — Aero, Aera, Aerora, Aeroga — where a plain
        // "aero" misses Aera entirely.
        ("Wind",    @"wind|aer[oa]|tornado|gale|cyclone|whirl|typhoon|hurricane"),
        ("Holy",    @"holy|pearl|banish|dia|divine|seraph"),
        ("Dark",    @"dark|shadow|night|doom|death|curse|evil|demi|bio|venom|poison"),
    ];

    // "Explode" is the NES-era translation of Self-Destruct and has to count as one, or an
    // FFII Bomb ends up offering the same suicide move twice under two names.
    private static readonly Regex SelfDestruct =
        new(@"self.?destruct|explode|exploder|blast|suicide|kamikaze", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Every combatant gets this, so a monster with no scraped abilities can still fight.</summary>
    private static readonly Move BasicAttack = new("Attack", null, MoveKind.Physical, 1.0);

    private const int MaxAbilityMoves = 3;

    /// <summary>
    /// The wiki's ability lists are transcriptions of each enemy's AI script, so alongside real
    /// moves they carry the script's own bookkeeping — "Unnamed script trigger", "Unhide enemy",
    /// "Flip sprite horizontally". Gilgamesh lists twenty-eight entries, four of which are
    /// stage directions. They are not moves and must never reach a button.
    /// </summary>
    private static readonly Regex ScriptInternal = new(
        @"unnamed|unhide|flip sprite|terminate battle|script trigger|clone of|^nothing$|^none$|^battle$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Buffs, heals and escapes. These deal no damage, so treating one as an attack invents a
    /// move the game never had — and because the picker takes the strongest option available,
    /// the invented move tends to win. A Final Fantasy IV Deathmask was beating Ifrit to death
    /// with "Reflect" for 19,320 a turn.
    /// </summary>
    private static readonly HashSet<string> SupportMoves = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reflect", "Protect", "Shell", "Wall", "Barrier", "Barrier Change", "Mighty Guard",
        "Haste", "Slow", "Stop", "Regen", "Remedy", "Esuna", "Dispel", "Float", "Levitate",
        "Blink", "Image", "Vanish", "Safe", "Aura", "Boost", "Focus", "Defend", "Guard",
        "Hide", "Escape", "Flee", "Run", "Scan", "Libra", "Peep", "Cover", "Entice",
        "Life", "Raise", "Arise", "Resurrection", "White Wind", "Grow",
    };

    // Cure, Cura, Curaga, Curaja, Heal, Healara, Healaga — the whole restorative line.
    private static readonly Regex HealingMove =
        new(@"^(cur(e|a|aga|aja|aja)|heal\w*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsSupport(string name) => SupportMoves.Contains(name) || HealingMove.IsMatch(name);

    public static IReadOnlyList<Move> Build(string? abilities)
    {
        var moves = new List<Move> { BasicAttack };

        if (string.IsNullOrWhiteSpace(abilities))
            return moves;

        var candidates = new List<string>();
        foreach (var name in abilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = Trim(name);
            if (trimmed.Length == 0 || ScriptInternal.IsMatch(trimmed) || IsSupport(trimmed)) continue;
            if (trimmed.Equals(BasicAttack.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) continue;

            candidates.Add(trimmed);
        }

        // An enemy with twenty usable moves only gets three buttons, so they go to the moves
        // that make a matchup a decision rather than a damage race: self-destruct first for its
        // risk, then the elemental ones. Article order would hand Gilgamesh "Flee" and "Jump"
        // and leave Omega's Atomic Ray unreachable.
        // Only one suicide button: it is a decision, and three of them is not a choice.
        var chosen = candidates.Where(c => SelfDestruct.IsMatch(c)).Take(1)
            .Concat(candidates.Where(c => !SelfDestruct.IsMatch(c) && ElementFor(c) is not null))
            .Concat(candidates.Where(c => !SelfDestruct.IsMatch(c) && ElementFor(c) is null))
            .Take(MaxAbilityMoves);

        moves.AddRange(chosen.Select(BuildOne));
        return moves;
    }

    private static Move BuildOne(string name)
    {
        // Self-destruct hits far harder than anything else the enemy has and takes the user
        // with it, so the player gets a real decision rather than an obviously-best button.
        if (SelfDestruct.IsMatch(name))
            return new Move(name, null, MoveKind.Magic, 2.6, Recoil: 0.5);

        var element = ElementFor(name);

        return element is null
            ? new Move(name, null, MoveKind.Physical, 1.15)
            : new Move(name, element, MoveKind.Magic, 1.3);
    }

    public static string? ElementFor(string abilityName)
    {
        foreach (var (element, pattern) in ElementPatterns)
            if (Regex.IsMatch(abilityName, pattern, RegexOptions.IgnoreCase))
                return element;

        return null;
    }

    // Ability values arrive with the article's parenthetical notes attached —
    // "Hit (Level 1 = Attack x 1.5)" — which are unreadable on a button. Final Fantasy V also
    // marks battle commands with a leading "!", and "!Attack" has to reduce to "Attack" or it
    // slips past the check that stops an enemy being handed two identical attack buttons.
    private static string Trim(string name)
    {
        var trimmed = Regex.Replace(name, @"\s*\([^)]*\)?\s*$", "").Trim();
        trimmed = trimmed.TrimStart('!', '*', '-', '\'', '"').Trim();
        trimmed = StripSpellLevel(trimmed);
        return trimmed.Length > 24 ? trimmed[..24].TrimEnd() : trimmed;
    }

    /// <summary>
    /// Drops the spell-level suffix Final Fantasy II tags onto ability names. Its Bomb lists
    /// "Self Destruct VII, Self Destruct 7, Explode7" — one move written three ways, which
    /// without this becomes three separate buttons that all do the same thing.
    /// </summary>
    private static string StripSpellLevel(string name)
    {
        // Roman numerals stay case-sensitive: an insensitive match would eat the trailing
        // letter of ordinary words, turning "Magic" into "Magi" and "Vanish" into "Vanish".
        var trimmed = Regex.Replace(name, @"\s+[IVX]{1,4}$", "");
        return Regex.Replace(trimmed, @"\s*\d+$", "").Trim();
    }
}
