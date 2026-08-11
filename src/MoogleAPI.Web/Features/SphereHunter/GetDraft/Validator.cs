using FastEndpoints;
using FluentValidation;

namespace MoogleAPI.Web.Features.SphereHunter.GetDraft;

public class Validator : Validator<GetDraftRequest>
{
    /// <summary>
    /// Bounded because the token is folded into a seed and echoed back — there is no reason for it
    /// to be long, and no reason to hash a megabyte of it.
    /// </summary>
    public const int MaxRunLength = 64;

    public Validator()
    {
        RuleFor(x => x.Run)
            .NotEmpty().WithMessage("A run token is required, e.g. run=7f3a9c21.")
            .MaximumLength(MaxRunLength);
    }
}
