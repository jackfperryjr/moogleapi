using MoogleAPI.Web.Infrastructure.Battle;

namespace MoogleAPI.Web.Infrastructure.SphereHunter;

/// <param name="Level">What both sides fight at on this floor. Progression, not a resource.</param>
/// <param name="Capture">
/// The fiend offered for sealing once the floor is cleared — the boss that ended it. Declining is
/// a real option: a party slot spent on it is a slot given up.
/// </param>
public record TowerFloor(
    int Number,
    int GameId,
    string GameName,
    int Level,
    IReadOnlyList<Sphere> Opponents,
    Sphere Capture);

public record SkippedFloor(int GameId, string GameName, string Reason);

public record Tower(
    string Run,
    IReadOnlyList<Sphere> Party,
    IReadOnlyList<TowerFloor> Floors,
    IReadOnlyList<SkippedFloor> Skipped);

/// <summary>
/// Builds a tower: the hand a party is drafted from, and the floors it climbs.
/// </summary>
/// <remarks>
/// Not a daily. Jack, 2026-08-11: <em>"If I lose, I want to try again."</em> A run is identified by
/// a token the client makes up, and everything about that run — which nine spheres are offered,
/// which fiends stand on each floor — is derived from it. Losing costs a token, not a day.
/// <para>
/// The token is the client's rather than the server's because the server keeps no run state. A
/// player who refreshes mid-climb has to be handed back the same tower, and deriving it from
/// something they hold is what makes that work without a session.
/// </para>
/// <para>
/// No secret is mixed in, unlike <see cref="Puzzles.DailyPuzzle"/>. That exists so nobody can
/// compute tomorrow's Kupodle answer; here the whole tower is in the response by design, so there
/// is nothing a predictable seed could give away.
/// </para>
/// </remarks>
public class TowerBuilder(SpherePool pool)
{
    /// <summary>One floor per battle-ready game, oldest first.</summary>
    public static int[] FloorGameIds => BattlePool.GameIds;

    /// <summary>Spheres offered at the draft. Nine is three parties' worth of choice.</summary>
    public const int DraftSize = 9;

    public const int PartySize = 3;

    /// <summary>Fights on a floor: two of the game's rank and file, then one of its bosses.</summary>
    public const int BattlesPerFloor = 3;

    /// <summary>
    /// Health and magic restored between floors, as a share of maximum.
    /// </summary>
    /// <remarks>
    /// Below what a floor costs, deliberately, so a run trends downward and the tower is a war of
    /// attrition rather than eleven independent fights. The same reasoning as Battle Square's
    /// wave recovery, and the same trap to avoid: set it at or above the cost of a floor and the
    /// run stops being one.
    /// </remarks>
    public const double FloorRecovery = 0.35;

    /// <summary>A fight decided in a click or two is not one.</summary>
    private const int MinTurns = 3;

    /// <summary>
    /// How far an opponent's health rating may sit from the party's best, so a floor is a fight
    /// rather than a formality in either direction. Bosses get more room — a boss is meant to be
    /// the wall at the end of a game, not a third random encounter.
    /// </summary>
    private const int RatingBand = 18;
    private const int BossRatingBand = 30;

    /// <summary>
    /// The hand a party is drafted from: nine spheres, no two from the same game, spread across
    /// the power range so the draft is a choice rather than a search for the biggest number.
    /// </summary>
    /// <remarks>
    /// One per game is what makes the hand read as a hand. Drawing nine uniformly from 2,274 gives
    /// four Final Fantasy XII entries and nothing from the first three games about a third of the
    /// time, because the pool is not evenly distributed and the eye reads that as a bug.
    /// </remarks>
    public async Task<IReadOnlyList<Sphere>> DraftAsync(string run, CancellationToken ct)
    {
        var draftable = await pool.DraftableAsync(ct);
        var rng = DeterministicRandom.ForScope(SeedFor(run, "draft"), "draft");

        var byGame = draftable
            .GroupBy(s => s.GameId)
            .OrderBy(g => g.Key)
            .ToList();

        var hand = new List<Sphere>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Walk the games in a shuffled order and take one from each, so which games appear varies
        // by day while no game ever appears twice.
        foreach (var game in Shuffle(byGame, rng))
        {
            if (hand.Count == DraftSize) break;

            // No two spheres in a hand share a name. The pool is ranked by how many games a
            // monster appears in, so the top of every bestiary is the same recurring cast — Bomb
            // is in ten of them — and without this a hand offers Bomb from Final Fantasy IV beside
            // Bomb from Final Fantasy IX, which reads as a bug rather than as a choice.
            var members = game.Where(s => !names.Contains(s.Name)).ToList();
            if (members.Count == 0) continue;

            var pick = members[rng.Next(members.Count)];
            hand.Add(pick);
            names.Add(pick.Name);
        }

        return [.. hand.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<Tower?> BuildAsync(IReadOnlyList<int> partyIds, string run, CancellationToken ct)
    {
        if (partyIds.Count is 0 or > PartySize) return null;

        var all = await pool.GetAsync(ct);
        var byId = all.ToDictionary(s => s.Id);

        var party = partyIds.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (party.Count != partyIds.Distinct().Count()) return null;

        var seed = SeedFor(run, $"tower:{string.Join(",", partyIds.OrderBy(i => i))}");
        var byGame = all.GroupBy(s => s.GameId).ToDictionary(g => g.Key, g => g.ToList());

        var floors = new List<TowerFloor>();
        var skipped = new List<SkippedFloor>();

        foreach (var gameId in FloorGameIds)
        {
            var bestiary = byGame.GetValueOrDefault(gameId, []);
            var level = SphereFactory.LevelForFloor(floors.Count + 1, FloorGameIds.Length);

            var opponents = PickOpponents(bestiary, party, seed, floors.Count + 1, level);
            if (opponents.Count < BattlesPerFloor)
            {
                skipped.Add(new SkippedFloor(gameId, NameOf(bestiary, gameId), "not enough comparable opponents"));
                continue;
            }

            floors.Add(new TowerFloor(
                floors.Count + 1, gameId, opponents[0].GameName, level, opponents,
                Capture: opponents[^1]));
        }

        return new Tower(run, party, floors, skipped);
    }

    /// <summary>
    /// Two of the game's rank and file, then one of its bosses — each of them a fight the party can
    /// actually win.
    /// </summary>
    private static List<Sphere> PickOpponents(
        List<Sphere> bestiary, IReadOnlyList<Sphere> party, ulong seed, int floor, int level)
    {
        var rng = DeterministicRandom.ForScope(seed, "floor", floor);
        var partyIds = party.Select(s => s.Id).ToHashSet();

        var enemies = Winnable(bestiary.Where(s => !s.IsBoss && !partyIds.Contains(s.Id)), party, level, RatingBand);
        var bosses = Winnable(bestiary.Where(s => s.IsBoss && !partyIds.Contains(s.Id)), party, level, BossRatingBand);

        var picked = new List<Sphere>();

        for (var i = 0; i < BattlesPerFloor - 1 && enemies.Count > 0; i++)
        {
            var index = rng.Next(enemies.Count);
            picked.Add(enemies[index]);
            enemies.RemoveAt(index);
        }

        // A game whose boss articles are too thin still ends on its hardest ordinary enemy, rather
        // than losing its floor from the tower altogether.
        if (bosses.Count > 0) picked.Add(bosses[rng.Next(bosses.Count)]);
        else if (enemies.Count > 0) picked.Add(enemies.OrderByDescending(s => s.Ratings.HitPoints).First());

        return picked;
    }

    /// <summary>
    /// Candidates the party can beat, and that will take long enough to be worth playing.
    /// </summary>
    /// <remarks>
    /// Vetted against the party's <em>best</em> answer to each opponent rather than an average,
    /// because the player gets to switch — a floor is fair if any of the three can handle it, and
    /// finding which one is the game. Rated on comparable health first so that a Goblin never draws
    /// a superboss, then on the same arithmetic the browser will resolve the fight with.
    /// </remarks>
    internal static List<Sphere> Winnable(
        IEnumerable<Sphere> candidates, IReadOnlyList<Sphere> party, int level, int band)
    {
        var target = party.Max(s => s.Ratings.HitPoints);

        var banded = candidates
            .Where(c => Math.Abs(c.Ratings.HitPoints - target) <= band)
            .ToList();

        if (banded.Count == 0)
        {
            banded = [.. candidates
                .OrderBy(c => Math.Abs(c.Ratings.HitPoints - target))
                .Take(8)];
        }

        var winnable = banded
            .Where(c =>
            {
                var toWin = party.Min(p => SphereMath.TurnsToKill(p, c, level));
                var toLose = party.Max(p => SphereMath.TurnsToKill(c, p, level));

                return toWin >= MinTurns && toWin <= toLose;
            })
            .ToList();

        if (winnable.Count > 0) return winnable;

        // Everything here is a losing matchup. Take the least bad rather than drop the floor: the
        // party has two more members, a Limit the estimate ignores, and the option to run the
        // fight with a sphere the estimate never considered best.
        return [.. banded
            .OrderBy(c => party.Min(p => SphereMath.TurnsToKill(p, c, level))
                          / (double)Math.Max(1, party.Max(p => SphereMath.TurnsToKill(c, p, level))))
            .Take(4)];
    }

    private static List<IGrouping<int, Sphere>> Shuffle(List<IGrouping<int, Sphere>> games, DeterministicRandom rng)
    {
        var shuffled = new List<IGrouping<int, Sphere>>(games);

        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }

    /// <summary>
    /// The run token folded into a seed. Scoped so the draft and the tower draw from independent
    /// streams — otherwise adding a floor would reshuffle which spheres were offered.
    /// </summary>
    private static ulong SeedFor(string run, string scope) =>
        (ulong)$"spherehunter:v1:{run}:{scope}".GetDeterministicHash();

    private static string NameOf(List<Sphere> bestiary, int gameId) =>
        bestiary.Count > 0 ? bestiary[0].GameName : $"Game {gameId}";
}
