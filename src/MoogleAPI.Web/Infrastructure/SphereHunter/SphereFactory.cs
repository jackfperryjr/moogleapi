using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <summary>
/// Seals a monster in a sphere: its published numbers become ratings, its affinity is read off its
/// resistances and its spell list, and its abilities become four buttons.
/// </summary>
public static class SphereFactory
{
    /// <summary>
    /// The level the ground hunt is fought at, and the level at the top.
    /// </summary>
    /// <remarks>
    /// The hunt is 15 rather than something rounder because damage carries a
    /// <c>(2 × level / 5 + 2)</c> term whose <c>+ 2</c> is a large share of a very low level and a
    /// rounding error at a high one. Health scales cleanly with level, that constant does not, and
    /// starting at 5 left the first hunt's fights visibly shorter than the rest of the expedition's.
    /// From 15 the whole expedition sits inside a turn or two of itself.
    /// </remarks>
    public const int MinLevel = 15;
    public const int MaxLevel = 80;

    /// <summary>The level everything on a given hunt fights at.</summary>
    /// <remarks>
    /// Both sides, deliberately. Level is a difficulty curve here rather than a resource the player
    /// accumulates — a party that out-levelled the hunt would turn the back half of the expedition into
    /// a formality, and the interesting decisions are which three spheres you brought and when you
    /// switch them, not whether you ground.
    /// </remarks>
    public static int LevelForHunt(int hunt, int hunts)
    {
        if (hunts <= 1) return MaxLevel;

        var progress = Math.Clamp((hunt - 1) / (double)(hunts - 1), 0, 1);
        return (int)Math.Round(MinLevel + (MaxLevel - MinLevel) * progress);
    }

    public static Sphere Seal(Fighter fighter, SphereScale scale)
    {
        var ratings = scale.For(fighter);
        var affinity = Elements.Affinity(fighter.Absorbs, fighter.Abilities, fighter.Weaknesses);

        return new Sphere(
            fighter.Id,
            fighter.Name,
            fighter.GameId,
            fighter.GameName,
            fighter.Category,
            fighter.ImageUrl,
            affinity,
            ratings,
            SphereMoves.MagicPointsFor(ratings.MagicAttack),
            [.. Elements.Parse(Elements.Split(fighter.Weaknesses)).Distinct()],
            [.. Elements.Parse(Elements.Split(fighter.Absorbs)).Distinct()],
            SphereMoves.For(fighter.Abilities, fighter.Name, affinity),
            fighter.Popularity);
    }
}
