using FastEndpoints;
using FluentValidation;

namespace MoogleAPI.Web.Features.Monsters.Search;

public class Validator : Validator<SearchMonstersRequest>
{
    public Validator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Search query is required.")
            .MinimumLength(2).WithMessage("Query must be at least 2 characters.")
            .MaximumLength(100).WithMessage("Query must not exceed 100 characters.");
    }
}
