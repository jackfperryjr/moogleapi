using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.Arena;

/// <summary>
/// A playable character reduced to what the arena needs, detached from the database.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="Models.Character"/>. The roster is cached, and HybridCache
/// serializes what it stores — an EF entity loaded with its <c>Game</c> navigation carries
/// <c>Game.Characters</c> back with it, so serializing one walks
/// <c>Character → Game → Characters → Game</c> until the serializer gives up at depth 64 and the
/// endpoint answers 400. Projecting to plain values in the query is what makes the cache safe,
/// and it keeps the whole games's character list from being materialized to read six fields.
/// </remarks>
public record ArenaCharacter(
    int Id,
    string Name,
    int GameId,
    string GameName,
    string? Job,
    string? Weapon,
    string? Abilities,
    string? ImageUrl,
    int Popularity);

/// <summary>
/// Builds the character the player takes into the arena: their stats at a level, and the moves
/// they bring.
/// </summary>
public static class ChampionBuilder
{
    /// <summary>
    /// Marks a combatant as a party member rather than an encounter, in the same field the
    /// battle pool uses for "Boss" and "Enemy".
    /// </summary>
    public const string ChampionCategory = "Character";

    /// <summary>
    /// What a champion's health is worth against their game's <em>median</em> enemy, from level
    /// 1 to level 99.
    /// </summary>
    /// <remarks>
    /// Health is the one stat not read off a percentile of the game's distribution, because that
    /// distribution has a tail nothing else has. Final Fantasy X's monsters run from 696 HP at
    /// the twentieth percentile to 2,000,000 at the ninety-fifth — Penance and the dark aeons —
    /// so a level 99 champion placed at the top of it came out with <em>five million health</em>.
    /// The median is the robust centre: it ignores the superbosses entirely and lands within
    /// sight of what the games actually give a party. Final Fantasy's median enemy has 212 HP,
    /// and six times that is about what a level 99 Warrior of Light really has.
    /// <para>
    /// Nothing mechanical rests on the number — damage is a share of the defender's own maximum,
    /// so health cancels out of every exchange — but it is the most-read figure on the screen,
    /// and five million of it is not a balance problem, it is a credibility one.
    /// </para>
    /// </remarks>
    private const double MinHitPointsOfMedian = 0.8;
    private const double MaxHitPointsOfMedian = 6.0;

    public static Fighter Build(ArenaCharacter character, int level, GameStatScale scale, string gameName)
    {
        var percentile = LevelCurve.PercentileFor(level);
        var archetype = ArchetypeReader.For(character.Job, character.Weapon, character.Abilities);
        var weights = ArchetypeReader.WeightsFor(archetype);

        // Health grows against the median rather than climbing the distribution, so a game's
        // superbosses cannot drag it into the millions. Every other stat is a percentile: they
        // are all compared against an opponent's matching stat, where being quoted on the game's
        // own spread is the whole point.
        var growth = MinHitPointsOfMedian
            + (MaxHitPointsOfMedian - MinHitPointsOfMedian) * LevelCurve.ProgressFor(level);

        return new Fighter(
            Id: character.Id,
            Name: character.Name,
            GameId: character.GameId,
            GameName: gameName,
            Category: ChampionCategory,
            HitPoints: Scale(scale.HitPointsAt(0.5), weights.HitPoints * growth),
            Attack: Scale(scale.AttackAt(percentile), weights.Attack),
            Defense: Scale(scale.DefenseAt(percentile), weights.Defense),
            MagicAttack: Scale(scale.MagicAttackAt(percentile), weights.MagicAttack),
            MagicDefense: Scale(scale.MagicDefenseAt(percentile), weights.MagicDefense),
            Speed: Scale(scale.SpeedAt(percentile), weights.Speed),
            // A party member has no published elemental affinity, and inventing one would be
            // the single biggest thing that could be got wrong here: weakness doubles damage,
            // so a guess puts the player's whole run on a coin flip nothing in the data backs.
            Weaknesses: null,
            Absorbs: null,
            Abilities: character.Abilities,
            ImageUrl: character.ImageUrl);
    }

    private static int Scale(int baseline, double weight) => Math.Max(1, (int)Math.Round(baseline * weight));

    public static Archetype ArchetypeOf(ArenaCharacter character) =>
        ArchetypeReader.For(character.Job, character.Weapon, character.Abilities);

    /// <summary>
    /// The buttons a character gets: their own scraped commands first, then the archetype's
    /// stock kit to fill out the bar.
    /// </summary>
    /// <remarks>
    /// The kit is not decoration. <see cref="MoveBuilder"/> reads moves off the wiki's ability
    /// field, and for characters that field is close to empty — Final Fantasy I, III, V and XV
    /// publish none at all, so a third of the roster would arrive with nothing but Attack. That
    /// is not a hard fight, it is a fight with no decisions in it, and elemental choice is the
    /// only decision this combat model has: the opponent's weaknesses are the whole matchup, and
    /// they cannot be exploited by a character with no elements to hit them with.
    /// <para>
    /// The kit is the Fire/Blizzard/Thunder line every game in the series ships, so a character
    /// given one is never given something their game did not have.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Move> MovesFor(ArenaCharacter character, Archetype archetype)
    {
        var moves = new List<Move>(MoveBuilder.Build(character.Abilities));

        foreach (var move in KitFor(archetype))
        {
            if (moves.Count >= MaxMoves) break;
            if (moves.Any(m => m.Name.Equals(move.Name, StringComparison.OrdinalIgnoreCase))) continue;

            moves.Add(move);
        }

        return moves.Take(MaxMoves).ToList();
    }

    /// <summary>
    /// Four, against the three a monster gets. The extra button is the one advantage the player
    /// holds over an opponent picked to be a fair match for them on paper.
    /// </summary>
    private const int MaxMoves = 4;

    private static IReadOnlyList<Move> KitFor(Archetype archetype) => archetype switch
    {
        // Three elements, so a mage always has an answer to whatever the wave is weak to.
        Archetype.Mage =>
        [
            new Move("Fire", "Fire", MoveKind.Magic, 1.30),
            new Move("Blizzard", "Ice", MoveKind.Magic, 1.30),
            new Move("Thunder", "Thunder", MoveKind.Magic, 1.30),
        ],
        // One element and a heavy swing: enough to punish a weakness, not enough to cover
        // every wave the way a mage does.
        Archetype.Warrior =>
        [
            new Move("Power Break", null, MoveKind.Physical, 1.30, Status: StatusEffect.Blind),
            new Move("Fire", "Fire", MoveKind.Magic, 1.05),
        ],
        Archetype.Scout =>
        [
            new Move("Poison Sting", null, MoveKind.Physical, 0.90, Status: StatusEffect.Poison),
            new Move("Thunder", "Thunder", MoveKind.Magic, 1.05),
        ],
        _ =>
        [
            new Move("Fire", "Fire", MoveKind.Magic, 1.15),
            new Move("Blizzard", "Ice", MoveKind.Magic, 1.15),
        ],
    };
}
