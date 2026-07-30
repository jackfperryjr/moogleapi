using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Web.Infrastructure.Puzzles;

/// <param name="MinPopularity">0–100 notability floor; also the difficulty dial.</param>
public record PuzzleFilters(int? GameId, int MinPopularity, bool RequireImage)
{
    /// <summary>
    /// Bumping this remaps every date to a different character, past and future. It exists
    /// because moving the puzzle day from UTC to Central shifted which calendar date an
    /// evening session belonged to, leaving one day duplicated. Only change it deliberately —
    /// it invalidates every historical answer and any shared result grid.
    /// </summary>
    private const string SeedVersion = "v2";

    /// <summary>
    /// Seed scope for this filter combination. Each combination is an independent puzzle
    /// series, so "easy" and "hard" mode don't track each other day to day.
    /// </summary>
    public string Scope => $"characters:{SeedVersion}:{GameId}:{MinPopularity}:{RequireImage}";
}

/// <summary>
/// Resolves the character behind a given day's puzzle.
/// </summary>
/// <remarks>
/// Shared deliberately: the reveal endpoint and the guess-checking endpoint must resolve the
/// identical character for the same date and filters. Duplicating this logic would let the two
/// drift apart, and the failure is silent — guesses would be scored against a different
/// character than the one the game is asking about.
/// </remarks>
public class DailyCharacterSelector(AppDbContext db, DailyPuzzle puzzle)
{
    public async Task<Character?> SelectAsync(DateOnly date, PuzzleFilters filters, CancellationToken ct)
    {
        var query = Filtered(filters);

        var total = await query.CountAsync(ct);
        if (total == 0) return null;

        var offset = (int)(puzzle.SeedFor(date, filters.Scope) % (ulong)total);

        // Ordering must be deterministic or the same seed lands on a different row.
        return await query
            .OrderBy(c => c.Id)
            .Skip(offset)
            .Take(1)
            .FirstOrDefaultAsync(ct);
    }

    private IQueryable<Character> Filtered(PuzzleFilters filters)
    {
        var query = db.Characters.Include(c => c.Game).AsQueryable();

        if (filters.GameId.HasValue)
            query = query.Where(c => c.GameId == filters.GameId.Value);

        if (filters.MinPopularity > 0)
            query = query.Where(c => c.Popularity >= filters.MinPopularity);

        if (filters.RequireImage)
            query = query.Where(c => c.ImageUrl != null);

        return query;
    }
}
