namespace MoogleAPI.Web.Infrastructure.Models;

public class Character
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Role { get; set; }
    public string? Affiliation { get; set; }
    public string? Race { get; set; }
    public string? Hometown { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Where <see cref="ImageUrl"/> came from before it was copied to our storage.</summary>
    public string? ImageSourceUrl { get; set; }

    /// <summary>Replacement artwork in the catalogue's house style, kept alongside the original.</summary>
    public string? GeneratedImageUrl { get; set; }

    /// <summary>
    /// What the artwork actually is — cutout, flat, line-art, screenshot, busy-background.
    /// Recorded so the regeneration pass can select its batch with a query instead of
    /// re-downloading the whole library to work it out again.
    /// </summary>
    public string? ImageKind { get; set; }

    /// <summary>
    /// Comma-separated signature commands, e.g. "Trance/Revert" or "Blk Mag, Focus" — the
    /// character's battle identity rather than a full ability list.
    /// </summary>
    public string? Abilities { get; set; }

    public int GameId { get; set; }

    /// <summary>
    /// Notability score, 0–100. Derived from <see cref="WikiPageLength"/> and
    /// <see cref="WikiBacklinks"/> — the games use it to avoid serving obscure
    /// walk-on NPCs as puzzle answers.
    /// </summary>
    public int Popularity { get; set; }

    // Raw wiki signals kept so Popularity can be re-scored without a re-scrape.
    public int? WikiPageLength { get; set; }
    public int? WikiBacklinks { get; set; }

    public Game Game { get; set; } = null!;
}
