using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// Seals a monster in a sphere: its published numbers become ratings, its affinity is read off its
/// resistances and its spell list, and its abilities become four buttons.
/// </summary>
public static class SphereFactory
{
    /// <summary>
    /// The floor a level is quoted at, and the ceiling. The tower runs sixteen floors, so a party
    /// climbing it grows from five to eighty — the shape of a real playthrough, and wide enough
    /// that the same move visibly changes value on the way up.
    /// </summary>
    public const int MinLevel = 5;
    public const int MaxLevel = 80;

    /// <summary>The level everything on a given floor fights at.</summary>
    /// <remarks>
    /// Both sides, deliberately. Level is a difficulty curve here rather than a resource the player
    /// accumulates — a party that out-levelled the floor would turn the back half of the tower into
    /// a formality, and the interesting decisions are which three spheres you brought and when you
    /// switch them, not whether you ground.
    /// </remarks>
    public static int LevelForFloor(int floor, int floors)
    {
        if (floors <= 1) return MaxLevel;

        var progress = Math.Clamp((floor - 1) / (double)(floors - 1), 0, 1);
        return (int)Math.Round(MinLevel + (MaxLevel - MinLevel) * progress);
    }

    public static Sphere Seal(Fighter fighter, SphereScale scale)
    {
        var ratings = scale.For(fighter);
        var affinity = Elements.Affinity(fighter.Absorbs, fighter.Abilities, fighter.Weaknesses);

        return new Sphere(
            fighter.Id,
            fighter.Name,
            fighter.GameName,
            fighter.Category,
            fighter.ImageUrl,
            affinity,
            ratings,
            ratings.MaxHealth,
            SphereMoves.MagicPointsFor(ratings.MagicAttack),
            [.. Elements.Parse(Elements.Split(fighter.Weaknesses)).Distinct()],
            [.. Elements.Parse(Elements.Split(fighter.Absorbs)).Distinct()],
            SphereMoves.For(fighter.Abilities, fighter.Name, affinity));
    }
}
