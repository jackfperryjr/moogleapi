using FastEndpoints;
using FluentValidation;

namespace MoogleAPI.Web.Features.Monsters.Search;

public class Validator : Validator<SearchMonstersRequest>
{
    public Validator()
    {
        // The message names the fix, because the common mistake is calling this endpoint with
        // only a gameId and expecting it to list that game's monsters.
        RuleFor(x => x.Query)
            .NotEmpty()
            .WithMessage("A search term is required, e.g. ?query=bomb. To list a whole game's monsters instead, use /api/monsters?gameId=4.")
            .MinimumLength(2).WithMessage("Query must be at least 2 characters.")
            .MaximumLength(100).WithMessage("Query must not exceed 100 characters.");
    }
}
