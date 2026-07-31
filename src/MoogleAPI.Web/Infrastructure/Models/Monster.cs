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

    // Battle stats, read from the enemy's stats infobox. Every one is nullable: the older
    // games' articles are inconsistent about which stats they list, and several games
    // (notably FFX) tabulate per-version stats the parser only reads the first block of.
    public int? HitPoints { get; set; }
    public int? MagicPoints { get; set; }
    public int? Level { get; set; }
    public int? Experience { get; set; }
    public int? Gil { get; set; }

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
