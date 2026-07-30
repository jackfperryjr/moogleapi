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
