using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.DailyGuess;

public class Validator : Validator<DailyGuessRequest>
{
    public Validator()
    {
        // Same rule as the reveal endpoint: scoring a guess against a future date would leak
        // tomorrow's answer one attribute at a time.
        RuleFor(x => x.Date)
            .Must(d => d is null || !DailyPuzzle.IsInFuture(d.Value))
            .WithMessage("Cannot guess against a future date.");

        RuleFor(x => x.GuessId)
            .GreaterThan(0).WithMessage("GuessId must be a valid character id.");

        RuleFor(x => x.GuessNumber)
            .InclusiveBetween(1, Endpoint.MaxGuesses)
            .WithMessage($"GuessNumber must be between 1 and {Endpoint.MaxGuesses}.");

        RuleFor(x => x.MinPopularity)
            .InclusiveBetween(0, 100).WithMessage("MinPopularity must be between 0 and 100.");
    }
}
