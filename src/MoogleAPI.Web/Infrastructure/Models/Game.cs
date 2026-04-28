namespace MoogleAPI.Web.Infrastructure.Models;

public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ReleaseYear { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<Character> Characters { get; set; } = [];
    public ICollection<Monster> Monsters { get; set; } = [];
}
