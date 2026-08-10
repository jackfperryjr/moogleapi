using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.Characters.DailySilhouette;

public class Validator : Validator<DailySilhouetteRequest>
{
    public Validator()
    {
        // The same load-bearing rule as the reveal endpoint, and it matters more here: a shape is
        // enough to name a character, so walking the date forward would harvest upcoming answers
        // just as surely as reading them.
        RuleFor(x => x.Date)
            .Must(d => d is null || !DailyPuzzle.IsInFuture(d.Value))
            .WithMessage("Cannot request a puzzle for a future date.");

        RuleFor(x => x.MinPopularity)
            .InclusiveBetween(0, 100).WithMessage("MinPopularity must be between 0 and 100.");
    }
}
