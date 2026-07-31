namespace MoogleAPI.Web.Infrastructure.Models;

public class Monster
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"Boss" or "Enemy" — taken from the wiki's per-game boss category.</summary>
    public string? Category { get; set; }

    /// <summary>Where the enemy is encountered, e.g. "Phantom Train; Bomb forest".</summary>
    public string? Location { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// Where <see cref="ImageUrl"/> was originally taken from, kept when the art is copied to
    /// our own storage. Provenance survives the move, and re-running the copy stays idempotent
    /// because a row already pointing at our bucket can be told apart from one that isn't.
    /// </summary>
    public string? ImageSourceUrl { get; set; }

    /// <summary>
    /// Replacement artwork generated in the catalogue's house style, stored alongside the
    /// original rather than over it so a batch can be compared before it goes live — and
    /// undone by clearing this column.
    /// </summary>
    public string? GeneratedImageUrl { get; set; }

    /// <summary>
    /// What the artwork actually is — cutout, flat, line-art, screenshot, busy-background.
    /// Recorded so the regeneration pass can select its batch with a query instead of
    /// re-downloading the whole library to work it out again.
    /// </summary>
    public string? ImageKind { get; set; }

    // Battle stats, read from the enemy's stats infobox. Every one is nullable: the older
    // games' articles are inconsistent about which stats they list, and several games
    // (notably FFX) tabulate per-version stats the parser only reads the first block of.
    public int? HitPoints { get; set; }
    public int? MagicPoints { get; set; }
    public int? Level { get; set; }
    public int? Experience { get; set; }
    public int? Gil { get; set; }

    // Combat stats. Games disagree on what to call these — FFVII's "dexterity", FFX's
    // "agility" and FFVI's "speed" are the same idea — so they're normalized to one name here.
    public int? Attack { get; set; }
    public int? Defense { get; set; }
    public int? MagicAttack { get; set; }
    public int? MagicDefense { get; set; }
    public int? Speed { get; set; }
    public int? Evasion { get; set; }

    /// <summary>Comma-separated moves the enemy uses in battle, e.g. "Blaze, Self-Destruct".</summary>
    public string? Abilities { get; set; }

    /// <summary>Comma-separated items the enemy drops when defeated.</summary>
    public string? Drops { get; set; }

    /// <summary>Comma-separated items that can be stolen from the enemy.</summary>
    public string? Steals { get; set; }

    /// <summary>Comma-separated elements the enemy takes extra damage from, e.g. "Ice, Water".</summary>
    public string? Weaknesses { get; set; }

    /// <summary>Comma-separated elements the enemy drains HP from, e.g. "Fire".</summary>
    public string? Absorbs { get; set; }

    public int GameId { get; set; }

    /// <summary>
    /// Notability score, 0–100. Derived from <see cref="WikiPageLength"/> and
    /// <see cref="WikiBacklinks"/> — scored the same way as <see cref="Character.Popularity"/>
    /// so the games can avoid serving a random unnamed random-encounter fiend as an answer.
    /// </summary>
    public int Popularity { get; set; }

    // Raw wiki signals kept so Popularity can be re-scored without a re-scrape.
    public int? WikiPageLength { get; set; }
    public int? WikiBacklinks { get; set; }

    public Game Game { get; set; } = null!;
}
