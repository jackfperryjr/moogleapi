using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.GetRun;

public class Validator : Validator<GetRunRequest>
{
    public Validator()
    {
        RuleFor(x => x.Run)
            .NotEmpty().WithMessage("A run token is required, e.g. run=7f3a9c21.")
            .MaximumLength(GetDraft.Validator.MaxRunLength);

        // Checked here rather than in the builder so a malformed party is a 400 explaining itself
        // rather than a 404 that reads as "no such run".
        RuleFor(x => x.Spheres)
            .Must(s => Endpoint.ParseIds(s).Count is > 0 and <= HuntBuilder.PartySize)
            .WithMessage($"Name between one and {HuntBuilder.PartySize} sphere ids, e.g. spheres=101,204,388.");
    }
}
