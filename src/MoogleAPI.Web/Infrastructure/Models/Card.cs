namespace MoogleAPI.Web.Infrastructure.Models;

/// <summary>
/// A Triple Triad card. Corner values are 1–10, where the games render 10 as "A".
/// </summary>
public class Card
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int Top { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }

    /// <summary>Fire, Ice, Thunder, Poison, Earth, Wind, Water, Holy — null for non-elemental cards.</summary>
    public string? Element { get; set; }

    /// <summary>Card level, 1–10. Doubles as the deck-building difficulty tier.</summary>
    public int Level { get; set; }

    /// <summary>Monster, Boss, GF, or Player.</summary>
    public string? CardClass { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Where <see cref="ImageUrl"/> came from before it was copied to our storage.</summary>
    public string? ImageSourceUrl { get; set; }
    public int GameId { get; set; }

    public Game Game { get; set; } = null!;
}
