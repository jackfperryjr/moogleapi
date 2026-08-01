using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Infrastructure.Arena;

namespace MoogleAPI.Web.Features.Arena.GetRun;

public class Validator : Validator<GetArenaRunRequest>
{
    public Validator()
    {
        RuleFor(x => x.CharacterId)
            .GreaterThan(0)
            .WithMessage("A character is required, e.g. ?characterId=245. Pick one from /api/arena/roster.");

        RuleFor(x => x.Level)
            .InclusiveBetween(LevelCurve.MinLevel, LevelCurve.MaxLevel)
            .When(x => x.Level.HasValue)
            .WithMessage($"Level must be between {LevelCurve.MinLevel} and {LevelCurve.MaxLevel}.");
    }
}
