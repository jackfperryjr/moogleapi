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

public record Climb(string Family, DateOnly Date, IReadOnlyList<BattleRung> Rungs, IReadOnlyList<SkippedGame> Skipped);

/// <summary>
/// The monster as it fights on the first rung — what the picker is actually choosing between.
/// </summary>
/// <remarks>
/// Taken from the earliest game the family appears in, which is the first rung in every case but
/// one: a rung is dropped when its game can't field three comparable opponents, and if that
/// happens to the earliest game the run really starts one game later with slightly different
/// numbers. Vetting the whole ladder to show a tooltip would cost a full run build per starter,
/// so the picker shows the earliest form and the climb begins wherever it can.
/// </remarks>
public record StarterForm(
    string GameName, int HitPoints, int Attack, int Defense, int MagicAttack, int MagicDefense, int Speed,
    IReadOnlyList<string> Weaknesses, IReadOnlyList<string> Absorbs, IReadOnlyList<string> Moves);

public record Starter(
    string Family, int GameCount, string? ImageUrl, IReadOnlyList<string> Games, StarterForm StartingForm);

/// <summary>
/// Builds a day's climb: a ladder of games, the player's form of their chosen monster in
/// each, and three opponents per rung.
/// </summary>
public class ClimbBuilder(BattlePool pool, DailyPuzzle puzzle)
{
    /// <inheritdoc cref="BattlePool.GameIds"/>
    public static int[] LadderGameIds => BattlePool.GameIds;

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
        var battlePool = await pool.GetAsync(ct);

        return battlePool
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(f => f.GameId).Distinct().Count() >= MinStarterGames)
            .Select(g => new Starter(
                Family: g.Key,
                GameCount: g.Select(f => f.GameId).Distinct().Count(),
                ImageUrl: g.OrderBy(f => f.GameId).Select(f => f.ImageUrl).FirstOrDefault(u => u is not null),
                Games: g.OrderBy(f => f.GameId).Select(f => f.GameName).Distinct().ToList(),
                StartingForm: FormOf(g.OrderBy(f => f.GameId).First())))
            .OrderByDescending(s => s.GameCount)
            .ThenBy(s => s.Family, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static StarterForm FormOf(Fighter f) => new(
        f.GameName, f.HitPoints, f.Attack, f.Defense, f.MagicAttack, f.MagicDefense, f.Speed,
        SplitList(f.Weaknesses), SplitList(f.Absorbs),
        MoveBuilder.Build(f.Abilities).Select(m => m.Name).ToList());

    /// <summary>Affinities are stored comma-separated for the plain data endpoints; a browser
    /// wants them as an array.</summary>
    public static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<Climb?> BuildAsync(string family, DateOnly date, CancellationToken ct)
    {
        var battlePool = await pool.GetAsync(ct);

        var forms = battlePool
            .Where(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.GameId);

        if (forms.Count == 0) return null;

        // The seed string keeps its original name on purpose. It is not shown anywhere; renaming
        // it would reshuffle every ladder in the game for a cosmetic reason.
        var seed = puzzle.SeedFor(date, $"gauntlet:v1:{family.ToLowerInvariant()}");
        var byGame = battlePool.GroupBy(f => f.GameId).ToDictionary(g => g.Key, g => g.ToList());
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

        return new Climb(forms.Values.First().Name, date, rungs, skipped);
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

    private static string GameNameFor(Dictionary<int, List<Fighter>> byGame, int gameId) =>
        byGame.TryGetValue(gameId, out var fighters) && fighters.Count > 0 ? fighters[0].GameName : $"Game {gameId}";

}
