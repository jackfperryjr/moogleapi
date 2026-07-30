using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.DailyGuess;

/// <summary>
/// Scores one Kupodle guess without disclosing the answer.
/// </summary>
/// <remarks>
/// The comparison runs here rather than in the browser so the day's answer never reaches the
/// client until the game is over. A player can still learn it by exhausting attempts, which is
/// the intended ending — what they cannot do is read it out of a network response on move one,
/// or compute any future day's answer.
/// </remarks>
public class Endpoint(AppDbContext db, DailyCharacterSelector selector)
    : Endpoint<DailyGuessRequest, DailyGuessResponse>
{
    public const int MaxGuesses = 6;

    public override void Configure()
    {
        Post("/characters/daily/guess");
        AllowAnonymous();
        Description(b => b
            .WithName("SubmitDailyGuess")
            .WithSummary("Score a guess against the day's character without revealing it")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(DailyGuessRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DailyPuzzle.Today;
        var filters = new PuzzleFilters(req.GameId, req.MinPopularity, req.RequireImage);

        var answer = await selector.SelectAsync(date, filters, ct);
        if (answer is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Guesses aren't restricted to the puzzle pool — a player may name any character, and
        // being told it's wrong is a legitimate outcome.
        var guess = await db.Characters
            .Include(c => c.Game)
            .FirstOrDefaultAsync(c => c.Id == req.GuessId, ct);

        if (guess is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var correct = guess.Id == answer.Id;
        var outOfGuesses = req.GuessNumber >= MaxGuesses;

        await Send.OkAsync(new DailyGuessResponse(
            Date: date,
            Correct: correct,
            GuessNumber: req.GuessNumber,
            GuessesAllowed: MaxGuesses,
            Guess: new GuessedCharacter(
                guess.Id, guess.Name, guess.ImageUrl, guess.Game.Name, guess.Game.ReleaseYear,
                guess.Race, guess.Hometown, guess.Role, guess.Affiliation),
            Comparison: Compare(guess, answer),
            Answer: correct || outOfGuesses
                ? new RevealedAnswer(answer.Id, answer.Name, answer.ImageUrl,
                                     answer.Game.Name, answer.Game.ReleaseYear)
                : null
        ), ct);
    }

    private static GuessComparison Compare(Character guess, Character answer) => new(
        GameName:    Match(guess.Game.Name, answer.Game.Name),
        ReleaseYear: Match(guess.Game.ReleaseYear, answer.Game.ReleaseYear),
        Race:        Match(guess.Race, answer.Race),
        Hometown:    Match(guess.Hometown, answer.Hometown),
        Role:        Match(guess.Role, answer.Role),
        Affiliation: Match(guess.Affiliation, answer.Affiliation));

    // Two absent values count as a match: it's the truthful comparison, and the client renders
    // the attribute as "—" either way.
    private static AttributeMatch Match(string? guess, string? answer) =>
        string.Equals(guess?.Trim(), answer?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? AttributeMatch.Correct
            : AttributeMatch.Incorrect;

    private static AttributeMatch Match(int guess, int answer) => answer.CompareTo(guess) switch
    {
        0  => AttributeMatch.Correct,
        > 0 => AttributeMatch.Higher,   // the answer's year is later than the guess
        _  => AttributeMatch.Lower
    };
}
