using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.Daily;

public class Validator : Validator<DailyCharacterRequest>
{
    public Validator()
    {
        // The load-bearing rule: without it a player could walk the date forward and harvest
        // every upcoming answer.
        RuleFor(x => x.Date)
            .Must(d => d is null || !DailyPuzzle.IsInFuture(d.Value))
            .WithMessage("Cannot request a puzzle for a future date.");

        RuleFor(x => x.MinPopularity)
            .InclusiveBetween(0, 100).WithMessage("MinPopularity must be between 0 and 100.");
    }
}
