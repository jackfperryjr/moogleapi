namespace MoogleAPI.Web.Infrastructure.Models;

public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ReleaseYear { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? Description { get; set; }

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
