using FastEndpoints;
using FluentValidation;

namespace MoogleAPI.Web.Features.Battle.GetRun;

public class Validator : Validator<GetRunRequest>
{
    public Validator()
    {
        RuleFor(x => x.Family)
            .NotEmpty()
            .WithMessage("A monster is required, e.g. ?family=Bomb. Pick one from /api/battle/starters.")
            .MaximumLength(200).WithMessage("Monster name must not exceed 200 characters.");
    }
}
