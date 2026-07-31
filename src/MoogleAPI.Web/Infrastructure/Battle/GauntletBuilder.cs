using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Infrastructure.Battle;

/// <summary>A monster reduced to what a battle needs.</summary>
public record Fighter(
    int Id,
    string Name,
    int GameId,
    string GameName,
    string? Category,
    int HitPoints,
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Speed,
    string? Weaknesses,
    string? Absorbs,
    string? Abilities,
    string? ImageUrl
)
{
    public bool IsBoss => Category == "Boss";
}

public record BattleRung(int Number, int GameId, string GameName, Fighter Player, IReadOnlyList<Fighter> Opponents);

public record SkippedGame(int GameId, string GameName, string Reason);

public record Gauntlet(string Family, DateOnly Date, IReadOnlyList<BattleRung> Rungs, IReadOnlyList<SkippedGame> Skipped);

public record Starter(string Family, int GameCount, string? ImageUrl, IReadOnlyList<string> Games);

/// <summary>
/// Builds a day's gauntlet: a ladder of games, the player's form of their chosen monster in
/// each, and three opponents per rung.
/// </summary>
public class GauntletBuilder(AppDbContext db, HybridCache cache, DailyPuzzle puzzle)
{
    /// <summary>
    /// The games a battle can actually take place in. Battles never cross games, so a rung is
    /// only playable when both sides come from the same one.
    /// </summary>
    /// <remarks>
    /// Absent for want of stats: VIII, whose enemy articles give HP as level-scaling
    /// coefficients rather than a number, and XI, XIV and XVI, which publish almost none.
    /// <para>
    /// II is absent for a different reason. It has HP and art for 166 monsters, so it looks
    /// playable — but exactly one of them has a listed elemental weakness, against 31–91% in
    /// every other game here. Elemental choice is the whole decision in a battle, so a rung in
    /// II is a damage race with nothing to think about, and it happened to be the opening rung
    /// for the most popular starter.
    /// </para>
    /// </remarks>
    public static readonly int[] LadderGameIds = [1, 3, 4, 5, 6, 7, 9, 10, 12, 13, 15];

    /// <summary>How many games a name must appear in, battle-ready, to be offered as a starter.</summary>
    private const int MinStarterGames = 5;

    private const int BattlesPerRung = 3;

    // An opponent within this band of the player's HP is a fight rather than a formality.
    // Without it a rung-one Goblin can draw Chaos and its 20,000 HP.
    private const double MinOpponentHp = 0.4;
    private const double MaxOpponentHp = 2.2;
    private const double MaxBossHp = 4.0;

    public async Task<IReadOnlyList<Starter>> GetStartersAsync(CancellationToken ct)
    {
        var pool = await GetPoolAsync(ct);

        return pool
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(f => f.GameId).Distinct().Count() >= MinStarterGames)
            .Select(g => new Starter(
                Family: g.Key,
                GameCount: g.Select(f => f.GameId).Distinct().Count(),
                ImageUrl: g.OrderBy(f => f.GameId).Select(f => f.ImageUrl).FirstOrDefault(u => u is not null),
                Games: g.OrderBy(f => f.GameId).Select(f => f.GameName).Distinct().ToList()))
            .OrderByDescending(s => s.GameCount)
            .ThenBy(s => s.Family, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Gauntlet?> BuildAsync(string family, DateOnly date, CancellationToken ct)
    {
        var pool = await GetPoolAsync(ct);

        var forms = pool
            .Where(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.GameId);

        if (forms.Count == 0) return null;

        var seed = puzzle.SeedFor(date, $"gauntlet:v1:{family.ToLowerInvariant()}");
        var byGame = pool.GroupBy(f => f.GameId).ToDictionary(g => g.Key, g => g.ToList());
        var moves = new MoveCache();

        var rungs = new List<BattleRung>();
        var skipped = new List<SkippedGame>();

        foreach (var gameId in LadderGameIds)
        {
            if (!forms.TryGetValue(gameId, out var player))
            {
                // The family has no form in this game. Skipping is reported rather than hidden,
                // so the client can show "no Final Fantasy VII form — advanced automatically"
                // instead of appearing to lose a rung.
                skipped.Add(new SkippedGame(gameId, GameNameFor(byGame, gameId), "no battle-ready form in this game"));
                continue;
            }

            var opponents = PickOpponents(byGame.GetValueOrDefault(gameId, []), player, seed, rungs.Count + 1, moves);
            if (opponents.Count < BattlesPerRung)
            {
                skipped.Add(new SkippedGame(gameId, player.GameName, "not enough comparable opponents"));
                continue;
            }

            rungs.Add(new BattleRung(rungs.Count + 1, gameId, player.GameName, player, opponents));
        }

        return new Gauntlet(forms.Values.First().Name, date, rungs, skipped);
    }

    /// <summary>
    /// Two rank-and-file enemies then a boss. The boss gets a wider HP allowance because it is
    /// supposed to be the wall at the end of the game, not a third random encounter.
    /// </summary>
    private static List<Fighter> PickOpponents(
        List<Fighter> gamePool, Fighter player, ulong seed, int rung, MoveCache moves)
    {
        var rng = DeterministicRandom.ForScope(seed, "rung", rung);

        var enemies = Winnable(gamePool.Where(f => !f.IsBoss && f.Id != player.Id), player, MaxOpponentHp, moves);
        var bosses = Winnable(gamePool.Where(f => f.IsBoss && f.Id != player.Id), player, MaxBossHp, moves);

        var picked = new List<Fighter>();

        for (var i = 0; i < BattlesPerRung - 1 && enemies.Count > 0; i++)
        {
            var index = rng.Next(enemies.Count);
            picked.Add(enemies[index]);
            enemies.RemoveAt(index);
        }

        // Games whose boss articles are thin fall back to the toughest ordinary enemy, so a
        // rung still ends on its hardest fight rather than being dropped from the ladder.
        if (bosses.Count > 0)
            picked.Add(bosses[rng.Next(bosses.Count)]);
        else if (enemies.Count > 0)
            picked.Add(enemies.OrderByDescending(f => f.HitPoints).First());

        return picked;
    }

    private static List<Fighter> InBand(IEnumerable<Fighter> candidates, Fighter player, double upper)
    {
        var lower = player.HitPoints * MinOpponentHp;
        var ceiling = player.HitPoints * upper;

        var banded = candidates.Where(f => f.HitPoints >= lower && f.HitPoints <= ceiling).ToList();
        if (banded.Count > 0) return banded;

        // Nothing in band — fall back to the closest by HP so the rung stays playable.
        return candidates
            .OrderBy(f => Math.Abs(f.HitPoints - player.HitPoints))
            .Take(8)
            .ToList();
    }

    /// <summary>
    /// Comparable HP is not the same as a fair fight. A Final Fantasy IV Deathmask has half
    /// Ifrit's health, which passes the band, but 26 times its defence — so it kills Ifrit in
    /// four turns while shrugging off everything Ifrit has. Matchups are therefore vetted with
    /// the same arithmetic the browser resolves them with: the player must be able to win it
    /// with sustainable moves, and it must last long enough to be a fight.
    /// </summary>
    private static List<Fighter> Winnable(
        IEnumerable<Fighter> candidates, Fighter player, double upperHp, MoveCache moves)
    {
        var banded = InBand(candidates, player, upperHp);
        var playerMoves = moves.For(player);

        var winnable = banded
            .Where(c =>
            {
                var toWin = BattleMath.TurnsToKill(player, playerMoves, c);
                var toLose = BattleMath.TurnsToKill(c, moves.For(c), player);

                return toWin >= MinTurns && toWin <= toLose;
            })
            .ToList();

        if (winnable.Count > 0) return winnable;

        // Every candidate is a losing matchup — take the least bad rather than drop the rung,
        // since the player still has retries and a self-destruct the estimate ignores.
        return banded
            .OrderBy(c => (double)BattleMath.TurnsToKill(player, playerMoves, c)
                          / Math.Max(1, BattleMath.TurnsToKill(c, moves.For(c), player)))
            .Take(4)
            .ToList();
    }

    /// <summary>A fight decided in a click or two isn't one; three turns is the floor.</summary>
    private const int MinTurns = 3;

    /// <summary>
    /// Move lists are derived by regex from each monster's abilities, and vetting a rung
    /// compares the player against every candidate in the game — hundreds of monsters, twice
    /// each. Building them once per run keeps that off the hot path.
    /// </summary>
    private sealed class MoveCache
    {
        private readonly Dictionary<int, IReadOnlyList<Move>> _moves = [];

        public IReadOnlyList<Move> For(Fighter fighter)
        {
            if (_moves.TryGetValue(fighter.Id, out var cached)) return cached;

            var built = MoveBuilder.Build(fighter.Abilities);
            _moves[fighter.Id] = built;
            return built;
        }
    }

    private static string GameNameFor(Dictionary<int, List<Fighter>> byGame, int gameId) =>
        byGame.TryGetValue(gameId, out var fighters) && fighters.Count > 0 ? fighters[0].GameName : $"Game {gameId}";

    /// <summary>
    /// Every monster that can fight, across the ladder's games. Loaded whole and cached: it is
    /// a few thousand small rows, and holding it in memory keeps a twelve-rung build to one
    /// query instead of twenty-four.
    /// </summary>
    private async Task<List<Fighter>> GetPoolAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            "battle:pool:v2",
            async token =>
            {
                var raw = await db.Monsters
                    .Where(m => LadderGameIds.Contains(m.GameId)
                                && m.HitPoints != null && m.HitPoints > 0
                                && m.ImageUrl != null
                                // Content that was never a real encounter. The wiki documents
                                // cut and debug entries as enemies — Final Fantasy VI's
                                // "Unnamed cutscene" is a dummied cutscene loader with 1 HP —
                                // and they're valid data, just not something to fight. They
                                // stay in the API and are excluded only from the battle pool.
                                && !m.Name.StartsWith("Unnamed")
                                && (m.Description == null
                                    || (!m.Description.Contains("dummied")
                                        && !m.Description.Contains("unused")
                                        && !m.Description.Contains("is a debug"))))
                    .OrderBy(m => m.Id)
                    .Select(m => new RawFighter(
                        m.Id, m.Name, m.GameId, m.Game.Name, m.Category, m.HitPoints!.Value,
                        m.Attack, m.Defense, m.MagicAttack, m.MagicDefense, m.Speed,
                        m.Weaknesses, m.Absorbs, m.Abilities, m.ImageUrl))
                    .ToListAsync(token);

                return FillGapsWithGameMedians(raw);
            },
            cancellationToken: ct) ?? [];

    /// <summary>
    /// Articles omit individual stats constantly — Final Fantasy II publishes no magic attack
    /// at all — so a monster missing one is still a fine opponent and shouldn't be excluded.
    /// What it can't have is a flat placeholder: a fixed default of 10 sits wildly above or
    /// below whatever scale the game actually uses, and a fight between one monster's real
    /// stat and another's placeholder becomes a blowout. An FFII Ogre Chief with a defaulted
    /// magic attack of 10 hit a Bomb's genuine magic defence of 4 for a third of its health a
    /// turn. Filling from the game's own median keeps both sides on the same scale.
    /// </summary>
    private static List<Fighter> FillGapsWithGameMedians(List<RawFighter> raw)
    {
        var fighters = new List<Fighter>(raw.Count);

        foreach (var game in raw.GroupBy(r => r.GameId))
        {
            // A stat is only ever compared against its opposite, so when a game publishes none
            // of one it borrows the other's scale rather than a constant. Final Fantasy II
            // lists no magic attack whatsoever but does list magic defence in single digits —
            // pairing them keeps that exchange even, where a flat 10 against a real 4 let every
            // FFII enemy hit for a third of the player's health per turn.
            var attack = Median(game, r => r.Attack, r => r.Defense);
            var defense = Median(game, r => r.Defense, r => r.Attack);
            var magicAttack = Median(game, r => r.MagicAttack, r => r.MagicDefense);
            var magicDefense = Median(game, r => r.MagicDefense, r => r.MagicAttack);
            var speed = Median(game, r => r.Speed, r => r.Speed);

            fighters.AddRange(game.Select(r => new Fighter(
                r.Id, r.Name, r.GameId, r.GameName, r.Category, r.HitPoints,
                r.Attack ?? attack, r.Defense ?? defense,
                r.MagicAttack ?? magicAttack, r.MagicDefense ?? magicDefense, r.Speed ?? speed,
                r.Weaknesses, r.Absorbs, r.Abilities, r.ImageUrl)));
        }

        return fighters.OrderBy(f => f.Id).ToList();
    }

    /// <summary>
    /// Median of the values a game publishes for a stat, falling back to its opposing stat's
    /// median, and to 10 only when the game publishes neither.
    /// </summary>
    private static int Median(IEnumerable<RawFighter> game, Func<RawFighter, int?> stat, Func<RawFighter, int?> opposite)
    {
        var fighters = game as IReadOnlyCollection<RawFighter> ?? game.ToList();

        return MedianOf(fighters, stat) ?? MedianOf(fighters, opposite) ?? 10;
    }

    private static int? MedianOf(IEnumerable<RawFighter> game, Func<RawFighter, int?> stat)
    {
        var values = game.Select(stat).Where(v => v is > 0).Select(v => v!.Value).OrderBy(v => v).ToList();
        return values.Count == 0 ? null : values[values.Count / 2];
    }

    private record RawFighter(
        int Id, string Name, int GameId, string GameName, string? Category, int HitPoints,
        int? Attack, int? Defense, int? MagicAttack, int? MagicDefense, int? Speed,
        string? Weaknesses, string? Absorbs, string? Abilities, string? ImageUrl);
}
