namespace MoogleAPI.Web.Infrastructure.Models;

public class Character
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Role { get; set; }
    public string? Affiliation { get; set; }
    public int GameId { get; set; }

    public Game Game { get; set; } = null!;
}
