namespace MoogleAPI.Web.Infrastructure.Models;

public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ReleaseYear { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a numbered entry or one of their direct sequels, as opposed to a spin-off.
    /// </summary>
    /// <remarks>
    /// Square Enix has never published a spin-off list, so this is a curation decision rather than
    /// a fact any source can be read for — which is why it is a hand-set flag and not derived from
    /// the title. The line drawn here puts the direct sequels on the main-series side: FFX-2,
    /// FFXIII-2, The After Years and the Compilation of FFVII are set in a numbered entry's world
    /// and share its cast, so splitting them off would separate Yuna from Yuna.
    /// <para>
    /// Nothing filters on it yet. It exists so that once spin-off rosters land, a query can ask
    /// for the numbered games without listing them by id — the puzzle games in particular draw
    /// their answer pools from the whole catalogue, and Tactics or Dissidia arriving would
    /// silently widen every one of them.
    /// </para>
    /// </remarks>
    public bool IsMainSeries { get; set; }

    /// <summary>
    /// The game's full logo — the wide lockup with the title text. Hand-uploaded through the
    /// dashboard rather than scraped: there is no per-game article the image pipeline reads, and
    /// a logo is a fixed piece of brand art, not something to search for.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// The square emblem — the artwork alone, without the title text. A separate column rather
    /// than a resize of <see cref="ImageUrl"/> because it is a different crop, not a smaller one:
    /// the full logo is wide and mostly text, which is illegible and badly proportioned wherever
    /// a square is wanted.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Where <see cref="ImageUrl"/> came from, matching <c>Monster.ImageSourceUrl</c>. Games are
    /// not in the copy or generation stages, so nothing reads this to decide what to re-fetch;
    /// it is kept so a logo's provenance is recorded the same way as every other image.
    /// </summary>
    public string? ImageSourceUrl { get; set; }

    public ICollection<Character> Characters { get; set; } = [];
    public ICollection<Monster> Monsters { get; set; } = [];
    public ICollection<Card> Cards { get; set; } = [];
}
