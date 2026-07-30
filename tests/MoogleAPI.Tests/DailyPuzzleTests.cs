using Microsoft.Extensions.Options;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Tests;

public class DailyPuzzleTests
{
    private static DailyPuzzle Puzzle(string secret) =>
        new(Options.Create(new DailyPuzzleOptions { Secret = secret }));

    private static readonly DateOnly SomeDay = new(2026, 7, 29);

    [Fact]
    public void SameDayAndScopeAlwaysYieldTheSameSeed()
    {
        var a = Puzzle("shared-secret").SeedFor(SomeDay, "characters");
        var b = Puzzle("shared-secret").SeedFor(SomeDay, "characters");

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentDaysYieldDifferentSeeds()
    {
        var puzzle = Puzzle("shared-secret");

        Assert.NotEqual(
            puzzle.SeedFor(SomeDay, "characters"),
            puzzle.SeedFor(SomeDay.AddDays(1), "characters"));
    }

    [Fact]
    public void DifferentScopesYieldDifferentSeeds()
    {
        var puzzle = Puzzle("shared-secret");

        Assert.NotEqual(
            puzzle.SeedFor(SomeDay, "characters:easy"),
            puzzle.SeedFor(SomeDay, "characters:hard"));
    }

    /// <summary>
    /// The property that stops players precomputing future answers: knowing the date and the
    /// algorithm is not enough without the key.
    /// </summary>
    [Fact]
    public void SeedIsUnreachableWithoutTheSecret()
    {
        Assert.NotEqual(
            Puzzle("the-real-secret").SeedFor(SomeDay, "characters"),
            Puzzle("a-guess").SeedFor(SomeDay, "characters"));
    }

    [Fact]
    public void TomorrowIsRejectedAndTodayIsAllowed()
    {
        Assert.True(DailyPuzzle.IsInFuture(DailyPuzzle.Today.AddDays(1)));
        Assert.False(DailyPuzzle.IsInFuture(DailyPuzzle.Today));
        Assert.False(DailyPuzzle.IsInFuture(DailyPuzzle.Today.AddDays(-1)));
    }
}
