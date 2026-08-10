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
    /// A flat black silhouette of the served artwork, drawn for Kupodle's answer frame.
    /// </summary>
    /// <remarks>
    /// Generated separately rather than derived from <see cref="ImageUrl"/>, because the artwork
    /// cannot produce one: it is a full painted illustration with a scene behind the figure, so
    /// thresholding it catches the scenery too, and darkening it either leaves the character
    /// plainly recognisable or leaves a black rectangle. The background is the worse half of the
    /// problem — Kupodle is narrowed by guessing the game, and an FFVII street behind the shape
    /// answers that before the first guess.
    /// <para>
    /// It never touches <see cref="ImageUrl"/>. Nothing is promoted, nothing falls back to it,
    /// and a null here simply leaves the frame holding its question mark.
    /// </para>
    /// </remarks>
    public string? SilhouetteImageUrl { get; set; }

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

    /// <summary>
    /// Whether the player controls this character in battle, taken from the "Playable" group of
    /// the game's character navbox on the wiki.
    /// </summary>
    /// <remarks>
    /// There is no category for this and no infobox field either. <c>|type=npc</c> exists but is
    /// set on barely a sixth of the non-playable rows, and the prose test — "is a playable
    /// character" — answers for the whole compilation rather than this game: it makes Jessie and
    /// the three Turks playable in <em>Final Fantasy VII</em> because they are in Remake, and
    /// Zack because he is in Crisis Core. The navbox is the only source scoped to the one game,
    /// and it is curated by hand, so it also settles the cases an automated rule gets wrong —
    /// Sephiroth is listed under "Temporary playable", which he is, for one flashback.
    /// </remarks>
    public bool IsPlayable { get; set; }

    /// <summary>Battle class from the infobox, e.g. "Black Mage", "Knight", "Sky Pirate".</summary>
    public string? Job { get; set; }

    /// <summary>
    /// What the character fights with, e.g. "Knuckles", "Staves", "Bows, staves".
    /// </summary>
    /// <remarks>
    /// Kept because it is the one battle-role signal nearly every playable article carries.
    /// <see cref="Job"/> is absent for about half of them — the job-system games have no fixed
    /// class to record, and Lightning's article lists neither — while the weapon is almost always
    /// there and says the same thing: knuckles are a monk, staves a mage, firearms a marksman.
    /// </remarks>
    public string? Weapon { get; set; }

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
