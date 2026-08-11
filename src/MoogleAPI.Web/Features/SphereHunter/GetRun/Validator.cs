using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Puzzles;
using MoogleAPI.Web.Infrastructure.SphereHunter;

namespace MoogleAPI.Web.Features.SphereHunter.GetRun;

public class Validator : Validator<GetRunRequest>
{
    public Validator()
    {
        RuleFor(x => x.Date)
            .Must(d => d is null || !DailyPuzzle.IsInFuture(d.Value))
            .WithMessage("Cannot request a run for a future date.");

        // Checked here rather than in the builder so a malformed party is a 400 explaining itself
        // rather than a 404 that reads as "no such run".
        RuleFor(x => x.Spheres)
            .Must(s => Endpoint.ParseIds(s).Count is > 0 and <= TowerBuilder.PartySize)
            .WithMessage($"Name between one and {TowerBuilder.PartySize} sphere ids, e.g. spheres=101,204,388.");
    }
}
