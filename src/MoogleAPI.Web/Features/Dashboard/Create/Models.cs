using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Features.Dashboard.Update;

namespace MoogleAPI.Web.Features.Dashboard.Create;

// Creating and editing take the same field set — the only difference is that there is no row id
// yet. Reusing the Browse records rather than declaring a near-identical "new row" shape is what
// lets the editor post the same JSON to both, and stops the two drifting apart a field at a time.

public class CreateCharacterRequest
{
    [FromBody] public CharacterEdit Fields { get; set; } = null!;
}

public class CreateMonsterRequest
{
    [FromBody] public MonsterEdit Fields { get; set; } = null!;
}

public class CreateGameRequest
{
    [FromBody] public GameEdit Fields { get; set; } = null!;
}

/// <param name="Warning">
/// Something worth knowing that is not a reason to refuse — a name that already exists in the
/// same game. Shown after the row is saved, because the row is saved.
/// </param>
public record CreateResponse<T>(T Row, string? Warning);

public class CreateCharacterValidator : Validator<CreateCharacterRequest>
{
    public CreateCharacterValidator()
    {
        RuleFor(x => x.Fields).NotNull();
        RuleFor(x => x.Fields.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(EditRules.MaxShortText);
        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
        RuleFor(x => x.Fields.Popularity).InclusiveBetween(0, 100);
        RuleFor(x => x.Fields.WikiPageLength).OptionalCount();
        RuleFor(x => x.Fields.WikiBacklinks).OptionalCount();
        RuleFor(x => x.Fields.GameId).GreaterThan(0).WithMessage("Pick a game.");
        RuleFor(x => x.Fields.ImageUrl).OptionalUrl();
        RuleFor(x => x.Fields.GeneratedImageUrl).OptionalUrl();
    }
}

public class CreateMonsterValidator : Validator<CreateMonsterRequest>
{
    public CreateMonsterValidator()
    {
        RuleFor(x => x.Fields).NotNull();
        RuleFor(x => x.Fields.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(EditRules.MaxShortText);
        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
        RuleFor(x => x.Fields.Popularity).InclusiveBetween(0, 100);
        RuleFor(x => x.Fields.WikiPageLength).OptionalCount();
        RuleFor(x => x.Fields.WikiBacklinks).OptionalCount();
        RuleFor(x => x.Fields.GameId).GreaterThan(0).WithMessage("Pick a game.");
        RuleFor(x => x.Fields.ImageUrl).OptionalUrl();
        RuleFor(x => x.Fields.GeneratedImageUrl).OptionalUrl();
        RuleFor(x => x.Fields.HitPoints).OptionalCount();
        RuleFor(x => x.Fields.MagicPoints).OptionalCount();
        RuleFor(x => x.Fields.Level).OptionalCount();
        RuleFor(x => x.Fields.Experience).OptionalCount();
        RuleFor(x => x.Fields.Gil).OptionalCount();
        RuleFor(x => x.Fields.Attack).OptionalCount();
        RuleFor(x => x.Fields.Defense).OptionalCount();
        RuleFor(x => x.Fields.MagicAttack).OptionalCount();
        RuleFor(x => x.Fields.MagicDefense).OptionalCount();
        RuleFor(x => x.Fields.Speed).OptionalCount();
        RuleFor(x => x.Fields.Evasion).OptionalCount();
    }
}

public class CreateGameValidator : Validator<CreateGameRequest>
{
    public CreateGameValidator()
    {
        RuleFor(x => x.Fields).NotNull();
        RuleFor(x => x.Fields.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(EditRules.MaxShortText);
        RuleFor(x => x.Fields.Platform).NotEmpty().WithMessage("Platform is required.").MaximumLength(EditRules.MaxShortText);
        RuleFor(x => x.Fields.ReleaseYear).InclusiveBetween(1980, DateTime.UtcNow.Year + 5).WithMessage("Release year looks wrong.");
        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
    }
}
