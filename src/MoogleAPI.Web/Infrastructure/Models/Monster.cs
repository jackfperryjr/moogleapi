namespace MoogleAPI.Web.Infrastructure.Models;

public class Monster
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int? HitPoints { get; set; }
    public int GameId { get; set; }

    public Game Game { get; set; } = null!;
}
