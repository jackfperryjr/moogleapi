using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Battle;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Infrastructure.Arena;

/// <param name="Cost">
/// Share of the champion's maximum health the wave is estimated to cost, under the same
/// arithmetic the browser resolves it with. What makes eight consecutive waves a run rather
/// than eight separate fights: nothing heals between them, so these add up.
/// </param>
public record Wave(int Number, Fighter Opponent, Handicap Handicap, int BattlePoints, double Cost);

public record Champion(
    int CharacterId, string Name, Archetype Archetype, string? Job, string? Weapon,
    int Level, Fighter Fighter, IReadOnlyList<Move> Moves);

public record ArenaRun(
    Champion Champion, int GameId, string GameName, DateOnly Date,
    int RecommendedLevel, IReadOnlyList<Wave> Waves);

/// <param name="RecommendedLevel">
/// The level this character clears the day's waves at with a little health to spare.
/// </param>
public record RosterEntry(
    int CharacterId, string Name, int GameId, string GameName, Archetype Archetype,
    string? Job, string? Weapon, string? ImageUrl, int Popularity, int RecommendedLevel);

/// <summary>
/// Builds a Battle Square run: one character, at a level, against eight consecutive waves of
/// their own game's monsters, with the slots taking something away between each.
/// </summary>
public class ArenaBuilder(AppDbContext db, BattlePool pool, HybridCache cache, DailyPuzzle puzzle)
{
    public const int WavesPerRun = 8;

    /// <summary>
    /// Share of the champion's health each wave is meant to cost, measured against a reference
    /// champion. Rising, and ending on a wall.
    /// </summary>
    /// <remarks>
    /// Absolute targets rather than positions in a ranked list. Picking the seventh-hardest
    /// eighth of the game's monsters says nothing about whether that fight is survivable — it
    /// depends entirely on how brutal the game's tail is, which varies enormously. A target of
    /// 0.5 means the same thing in every game: a fight that costs half your health.
    /// <para>
    /// Calibrated so that a champion at <see cref="ReferenceLevel"/> — the level the waves are
    /// chosen against — finishes the run on about 17% health. That is what puts the
    /// recommendation near the middle of the level range instead of at one end: targets only
    /// slightly higher drain faster than <see cref="WaveRecovery"/> refills, no level below 60
    /// survives, and the bottom half of the range becomes unusable.
    /// </para>
    /// </remarks>
    private static readonly double[] WaveCostTargets = [0.14, 0.17, 0.20, 0.23, 0.27, 0.31, 0.36, 0.46];

    /// <summary>
    /// Health restored between waves, as a share of the champion's maximum.
    /// </summary>
    /// <remarks>
    /// Without this the format does not work, and it is worth being precise about why.
    /// <see cref="BattleMath"/> spends 30% of the defender's maximum health per hit and clamps
    /// the attack-to-defence ratio to [0.2, 0.8], which puts a floor of roughly 0.23 on what any
    /// fight can cost the winner — four turns to land a kill against the seventeen the loser
    /// needed. Eight of those in a row cannot total less than about 1.9 of a champion's health,
    /// at any level, against any opponents. Eight consecutive waves with no recovery is not a
    /// hard run; it is an impossible one, and no amount of levelling reaches it.
    /// <para>
    /// So the run restores a fixed share between waves. It is deliberately less than the waves
    /// cost, so health still trends down over eight fights and the last ones are fought on
    /// whatever is left — which is the thing the Battle Square is actually about.
    /// </para>
    /// </remarks>
    public const double WaveRecovery = 0.20;

    /// <summary>
    /// How much health the run has to end with for a level to count as clearing it. Above zero
    /// because the estimate ignores the handicaps, and the reel can halve the player's health
    /// outright — a level that finishes on nothing cannot survive its own slot machine.
    /// </summary>
    private const double SurvivalMargin = 0.12;

    /// <summary>
    /// The level opponents are ranked against, so a wave's difficulty is a property of the game
    /// rather than of the level the player happens to have picked.
    /// </summary>
    /// <remarks>
    /// Ranking against the player's own level would make the ladder circular: every level would
    /// find its own perfectly-matched opponents, no level would ever be too low, and the
    /// recommendation would have nothing to say.
    /// </remarks>
    private const int ReferenceLevel = 50;

    /// <summary>A fight decided in a click or two isn't one; three turns is the floor.</summary>
    private const int MinTurns = 3;

    /// <summary>
    /// The characters that can enter, each with the level that clears the day's waves.
    /// </summary>
    /// <remarks>
    /// Cached whole, per day. Every entry costs a full difficulty ranking of its game's pool —
    /// hundreds of monsters scored both ways — followed by a 99-level solve, and there are
    /// roughly eighty-five of them. That is far too much to repeat on each call to a page whose
    /// first action is to load it.
    /// </remarks>
    public async Task<IReadOnlyList<RosterEntry>> GetRosterAsync(int? gameId, DateOnly date, CancellationToken ct)
    {
        var roster = await cache.GetOrCreateAsync(
            $"arena:levels:v1:{date:yyyy-MM-dd}",
            async token => await BuildRosterAsync(date, token),
            cancellationToken: ct) ?? [];

        return gameId.HasValue ? roster.Where(r => r.GameId == gameId.Value).ToList() : roster;
    }

    private async Task<List<RosterEntry>> BuildRosterAsync(DateOnly date, CancellationToken ct)
    {
        var battlePool = await pool.GetAsync(ct);
        var characters = await GetPlayableAsync(ct);

        var byGame = battlePool.GroupBy(f => f.GameId).ToDictionary(g => g.Key, g => (IReadOnlyList<Fighter>)g.ToList());
        var roster = new List<RosterEntry>();

        foreach (var character in characters)
        {
            if (!byGame.TryGetValue(character.GameId, out var gamePool)) continue;

            var scale = GameStatScale.For(gamePool);
            if (scale is null) continue;

            // The day's real seed, so the level quoted here is the level the run actually needs.
            // Ranking against an arbitrary one would put a number on the picker that the run
            // then disagreed with.
            var waves = BuildWaves(character, gamePool, scale, SeedFor(date, character.GameId), ReferenceLevel);
            if (waves.Count < WavesPerRun) continue;

            roster.Add(new RosterEntry(
                character.Id, character.Name, character.GameId, character.GameName,
                ChampionBuilder.ArchetypeOf(character.Entity), character.Job, character.Weapon,
                character.ImageUrl, character.Popularity,
                RecommendedLevel(character, scale, waves)));
        }

        return roster
            .OrderBy(r => r.GameId)
            .ThenByDescending(r => r.Popularity)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ulong SeedFor(DateOnly date, int gameId) => puzzle.SeedFor(date, $"arena:v1:{gameId}");

    public async Task<ArenaRun?> BuildAsync(int characterId, int? level, DateOnly date, CancellationToken ct)
    {
        var character = (await GetPlayableAsync(ct)).FirstOrDefault(c => c.Id == characterId);
        if (character is null) return null;

        var battlePool = await pool.GetAsync(ct);
        var gamePool = battlePool.Where(f => f.GameId == character.GameId).ToList();

        var scale = GameStatScale.For(gamePool);
        if (scale is null) return null;

        // Waves are ranked against the character who will fight them, so a mage and a warrior in
        // the same game get ladders calibrated to each rather than one shared list — the point of
        // the ranking is that a fight is as hard as it is *for you*. The day fixes the draw, so
        // the same character on the same day always meets the same eight.
        var seed = SeedFor(date, character.GameId);

        var opponents = BuildWaves(character, gamePool, scale, seed, ReferenceLevel);
        if (opponents.Count < WavesPerRun) return null;

        var recommended = RecommendedLevel(character, scale, opponents);
        var chosen = Math.Clamp(level ?? recommended, LevelCurve.MinLevel, LevelCurve.MaxLevel);

        var champion = BuildChampion(character, chosen, scale);
        var moves = new MoveCache();
        moves.Set(champion.Fighter, champion.Moves);

        var waves = new List<Wave>(WavesPerRun);
        for (var i = 0; i < opponents.Count; i++)
        {
            var opponent = opponents[i];
            var handicap = HandicapReel.For(seed, i + 1);
            var cost = CostOf(champion.Fighter, champion.Moves, opponent, moves);

            waves.Add(new Wave(i + 1, opponent, handicap, PointsFor(i + 1, handicap), cost));
        }

        return new ArenaRun(champion, character.GameId, character.GameName, date, recommended, waves);
    }

    /// <summary>
    /// Points for clearing a wave: later waves are worth more, and a handicap pays for itself.
    /// </summary>
    private static int PointsFor(int waveNumber, Handicap handicap) =>
        (int)Math.Round(1000 * WaveCostTargets[waveNumber - 1] * handicap.Multiplier);

    private static Champion BuildChampion(PlayableCharacter character, int level, GameStatScale scale)
    {
        var archetype = ChampionBuilder.ArchetypeOf(character.Entity);
        var fighter = ChampionBuilder.Build(character.Entity, level, scale, character.GameName);

        return new Champion(
            character.Id, character.Name, archetype, character.Job, character.Weapon,
            level, fighter, ChampionBuilder.MovesFor(character.Entity, archetype));
    }

    /// <summary>
    /// Picks eight opponents of rising difficulty, ending on a boss.
    /// </summary>
    /// <remarks>
    /// Difficulty is measured, not guessed at: every candidate is scored with the same
    /// arithmetic the browser resolves the fight with, against a reference champion, and the
    /// waves are drawn from rising points in that ranking. Ranking by hit points instead —
    /// the obvious shortcut — would order them by nothing, because
    /// <see cref="BattleMath.DamagePerHit"/> takes damage as a share of the defender's own
    /// maximum HP and a monster's health cancels straight back out of the exchange.
    /// </remarks>
    private static List<Fighter> BuildWaves(
        PlayableCharacter character, IReadOnlyList<Fighter> gamePool, GameStatScale scale, ulong seed, int referenceLevel)
    {
        var reference = BuildChampion(character, referenceLevel, scale);
        var moves = new MoveCache();
        moves.Set(reference.Fighter, reference.Moves);

        var ranked = gamePool
            .Where(f => f.Id != character.Id)
            .Select(f => (Fighter: f, Cost: CostOf(reference.Fighter, reference.Moves, f, moves)))
            // A fight the reference champion cannot win at all is not a difficulty, it is a wall.
            // Excluded here rather than clamped, so the ramp spans fights that are actually fights.
            .Where(x => x.Cost is > 0 and < 1.0)
            .OrderBy(x => x.Cost)
            .ToList();

        var regular = ranked.Where(x => !x.Fighter.IsBoss).ToList();
        var bosses = ranked.Where(x => x.Fighter.IsBoss).ToList();

        if (regular.Count < WavesPerRun - 1) return [];

        var rng = DeterministicRandom.ForScope(seed, "arena", character.GameId);
        var costs = ranked.ToDictionary(x => x.Fighter.Id, x => x.Cost);
        var waves = new List<Fighter>(WavesPerRun);
        var used = new HashSet<int>();

        for (var i = 0; i < WavesPerRun - 1; i++)
        {
            var pick = PickNear(regular, WaveCostTargets[i], rng, used);
            if (pick is null) return [];

            waves.Add(pick);
            used.Add(pick.Id);
        }

        // A game can be too thin at some point on the ramp to supply the wave that belongs
        // there — Final Fantasy IV has almost nothing cheap — and the fallback then reaches for
        // whatever is nearest, which can be harder than the wave after it. Sorting by what was
        // actually picked keeps the run getting harder even when the pool couldn't hit the mark.
        waves.Sort((a, b) => costs[a.Id].CompareTo(costs[b.Id]));

        // The eighth is the wall, and it has to actually be one. A boss is preferred, but only
        // among those at least as costly as the wave before it: Final Fantasy IV publishes no
        // boss anywhere near the finale target, and taking the nearest regardless put Golbez on
        // the end of the run at less than half the cost of wave seven — a ladder that finished
        // on its easiest fight. Failing that, the ordinary pool supplies the wall instead.
        var floor = costs[waves[^1].Id];

        var finalists = bosses.Where(x => x.Cost >= floor).ToList();
        if (finalists.Count == 0) finalists = regular.Where(x => x.Cost >= floor && !used.Contains(x.Fighter.Id)).ToList();
        if (finalists.Count == 0) finalists = bosses.Count > 0 ? bosses : regular;

        var final = PickNear(finalists, WaveCostTargets[^1], rng, used);
        if (final is null) return [];

        waves.Add(final);
        return waves;
    }

    /// <summary>
    /// Takes a fighter costing about as much as the target, jittered so the same game does not
    /// serve the same ladder every day.
    /// </summary>
    private static Fighter? PickNear(
        List<(Fighter Fighter, double Cost)> ranked, double target, DeterministicRandom rng, HashSet<int> used)
    {
        var available = ranked.Where(x => !used.Contains(x.Fighter.Id)).ToList();
        if (available.Count == 0) return null;

        // Everything within a small band of the target, so there is something to choose between.
        // Falls back to the nearest handful when the game has nothing at this difficulty at all —
        // a wave slightly off target beats no run.
        var window = available.Where(x => Math.Abs(x.Cost - target) <= CostBand).ToList();
        if (window.Count == 0)
            window = available.OrderBy(x => Math.Abs(x.Cost - target)).Take(5).ToList();

        return window[rng.Next(window.Count)].Fighter;
    }

    /// <summary>How far from its target a wave may land and still be that wave.</summary>
    private const double CostBand = 0.05;

    /// <summary>
    /// Share of the champion's health one wave costs.
    /// </summary>
    /// <remarks>
    /// Both sides deal damage as a share of the defender's maximum HP, so the turns each needs
    /// to finish the other is a ratio in the same units on both sides: taking four turns to win
    /// a fight the opponent would win in twenty costs a fifth of the champion's health. That is
    /// what makes eight waves addable, and it is the whole basis of the level recommendation.
    /// </remarks>
    private static double CostOf(Fighter champion, IReadOnlyList<Move> championMoves, Fighter opponent, MoveCache moves)
    {
        var toWin = BattleMath.TurnsToKill(champion, championMoves, opponent);
        var toLose = BattleMath.TurnsToKill(opponent, moves.For(opponent), champion);

        // Unwinnable: the champion has nothing that damages this opponent.
        if (toWin == int.MaxValue) return double.PositiveInfinity;

        // A fight decided in one or two turns isn't one. Charged at the floor so a walkover
        // still costs the run something and can't be a free wave.
        if (toLose == int.MaxValue) return toWin < MinTurns ? 0.01 : 0.02;

        return toWin / (double)toLose;
    }

    /// <summary>
    /// The lowest level that clears the day's eight waves with health left over.
    /// </summary>
    /// <remarks>
    /// Solved rather than guessed, using the arithmetic the fights actually resolve with — the
    /// same principle the climb vets its rungs on. A level table would have to be per-game and
    /// would go stale the moment a scrape changed a stat.
    /// </remarks>
    private static int RecommendedLevel(PlayableCharacter character, GameStatScale scale, List<Fighter> waves)
    {
        var moves = new MoveCache();
        var best = LevelCurve.MaxLevel;
        var bestRemaining = double.NegativeInfinity;

        for (var level = LevelCurve.MinLevel; level <= LevelCurve.MaxLevel; level++)
        {
            var champion = BuildChampion(character, level, scale);
            moves.Set(champion.Fighter, champion.Moves);

            var remaining = SimulateRun(champion, waves, moves);

            // The first level that clears wins. The recommendation is the cheapest way through,
            // not the most comfortable one — going higher is the player's to choose.
            if (remaining >= SurvivalMargin) return level;

            if (remaining > bestRemaining) (best, bestRemaining) = (level, remaining);
        }

        // No level clears it — the game's numbers don't allow it. Offer the one that gets
        // furthest rather than nothing.
        return best;
    }

    /// <summary>
    /// Plays the run out at one level and returns the health left at the end, as a share of
    /// maximum. Negative means the run ended early.
    /// </summary>
    /// <remarks>
    /// Simulated rather than summed, because the waves are not independent: recovery is capped
    /// at full health, so banking an easy early wave is worth nothing, and a total that looks
    /// survivable can still contain a wave that kills. Handicaps are left out — see
    /// <see cref="SurvivalMargin"/> for what pays for them.
    /// </remarks>
    private static double SimulateRun(Champion champion, List<Fighter> waves, MoveCache moves)
    {
        var health = 1.0;

        for (var i = 0; i < waves.Count; i++)
        {
            health -= CostOf(champion.Fighter, champion.Moves, waves[i], moves);
            if (health <= 0) return health;

            if (i < waves.Count - 1)
                health = Math.Min(1.0, health + WaveRecovery);
        }

        return health;
    }

    /// <summary>A playable character with the fields the arena needs, and the row behind them.</summary>
    private record PlayableCharacter(
        int Id, string Name, int GameId, string GameName, string? Job, string? Weapon,
        string? ImageUrl, int Popularity, Models.Character Entity);

    private async Task<List<PlayableCharacter>> GetPlayableAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            "arena:roster:v1",
            async token =>
            {
                var characters = await db.Characters
                    .Include(c => c.Game)
                    .Where(c => c.IsPlayable && c.ImageUrl != null && BattlePool.GameIds.Contains(c.GameId))
                    .OrderBy(c => c.GameId).ThenBy(c => c.Name)
                    .ToListAsync(token);

                return characters
                    .Select(c => new PlayableCharacter(
                        c.Id, c.Name, c.GameId, c.Game.Name, c.Job, c.Weapon, c.ImageUrl, c.Popularity, c))
                    .ToList();
            },
            cancellationToken: ct) ?? [];
}
