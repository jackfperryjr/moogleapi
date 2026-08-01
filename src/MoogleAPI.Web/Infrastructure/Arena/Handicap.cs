using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.Arena;

/// <summary>What the slots take away from the player before a wave.</summary>
public enum HandicapKind
{
    /// <summary>The slot the original stops on often enough to be worth having.</summary>
    None,

    /// <summary>Halves current HP. Never below one, so it maims rather than kills.</summary>
    HalveHitPoints,

    /// <summary>Physical moves land for a fraction of their damage.</summary>
    Blind,

    /// <summary>Magic moves are locked out for the wave.</summary>
    Silence,

    /// <summary>Bleeds a share of maximum HP after the player acts.</summary>
    Poison,

    /// <summary>Every move but the basic Attack is locked for the wave.</summary>
    SealAbilities,

    /// <summary>Attack and Magic Attack are cut for the wave.</summary>
    WeakenOffence,

    /// <summary>Defence and Magic Defence are cut for the wave.</summary>
    StripDefence,
}

/// <param name="Multiplier">
/// What clearing the wave under this handicap is worth, applied to its battle points.
/// </param>
public record Handicap(HandicapKind Kind, string Name, string Description, double Multiplier)
{
    public StatusEffect Status => Kind switch
    {
        HandicapKind.Blind => StatusEffect.Blind,
        HandicapKind.Poison => StatusEffect.Poison,
        HandicapKind.Silence => StatusEffect.Silence,
        _ => StatusEffect.None,
    };
}

/// <summary>
/// The slot reel from the Gold Saucer's Battle Square, which spins between fights and takes
/// something away from the player before the next one.
/// </summary>
/// <remarks>
/// Handicaps that would end the run outright are absent. The original can strip your materia or
/// leave you at 1 HP because it hands back a full heal and a save point afterwards; here a wave
/// carries its damage into the next, so an equally harsh reel would just be an unannounced loss.
/// Each of these costs the player something they can still play around.
/// <para>
/// The multipliers are the compensation, and they are what makes the reel a mechanic rather than
/// a tax: a run that draws badly and survives scores far above one that never got hit.
/// </para>
/// </remarks>
public static class HandicapReel
{
    public static readonly Handicap None =
        new(HandicapKind.None, "No handicap", "The reel comes up empty. Fight as you are.", 1.0);

    private static readonly Handicap[] Reel =
    [
        None,
        new(HandicapKind.HalveHitPoints, "Lucky 7 (broken)", "Your current HP is halved.", 1.9),
        new(HandicapKind.Blind, "Darkness", "Your physical moves land for half damage.", 1.5),
        new(HandicapKind.Silence, "Silence", "Your magic is locked for this wave.", 1.6),
        new(HandicapKind.Poison, "Poison", "You bleed health after every turn you take.", 1.4),
        new(HandicapKind.SealAbilities, "Materia broken", "Everything but Attack is locked for this wave.", 2.0),
        new(HandicapKind.WeakenOffence, "Power down", "Your Attack and Magic Attack are cut by a third.", 1.5),
        new(HandicapKind.StripDefence, "Armor broken", "Your Defense and Magic Defense are cut by a third.", 1.7),
    ];

    /// <summary>How much a weakened or stripped stat keeps.</summary>
    public const double StatPenalty = 0.66;

    /// <summary>
    /// The reel never spins before the first wave — a run that opens on "Materia broken" is
    /// decided before the player has made a single choice.
    /// </summary>
    public static Handicap For(ulong seed, int waveNumber)
    {
        if (waveNumber <= 1) return None;

        var rng = DeterministicRandom.ForScope(seed, "handicap", waveNumber);
        return Reel[rng.Next(Reel.Length)];
    }

    public static IReadOnlyList<Handicap> All => Reel;
}
