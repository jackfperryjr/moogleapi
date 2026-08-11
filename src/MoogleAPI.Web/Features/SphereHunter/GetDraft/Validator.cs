using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Puzzles;

namespace MoogleAPI.Web.Features.SphereHunter.GetDraft;

public class Validator : Validator<GetDraftRequest>
{
    public Validator()
    {
        RuleFor(x => x.Date)
            .Must(d => d is null || !DailyPuzzle.IsInFuture(d.Value))
            .WithMessage("Cannot request a draft for a future date.");
    }
}
