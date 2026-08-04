using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Features.Dashboard.Browse;

namespace MoogleAPI.Web.Features.Dashboard.Update;

// The row id comes from the route and the field set from the body, so the two can never
// disagree — there is no id inside the payload for a stale editor to send.

public class UpdateCharacterRequest
{
    public int Id { get; set; }
    [FromBody] public CharacterEdit Fields { get; set; } = null!;
}

public class UpdateMonsterRequest
{
    public int Id { get; set; }
    [FromBody] public MonsterEdit Fields { get; set; } = null!;
}

public class UpdateGameRequest
{
    public int Id { get; set; }
    [FromBody] public GameEdit Fields { get; set; } = null!;
}

/// <summary>What the row looks like after the write, so the table can redraw from the response.</summary>
public record UpdateResponse<T>(T Row);

// ── Validation ────────────────────────────────────────────────────────────────
// Curation is hand-typing, so these rules exist to catch slips, not to enforce a schema the
// scraper already satisfies. They stay permissive about what the wiki produced — half these
// columns are null across most of the library — and strict only where a bad value would break
// something downstream: an empty name leaves an unfindable row, a popularity outside 0–100
// silently changes which characters the puzzle pool will serve.

public static class EditRules
{
    public const int MaxShortText = 500;
    public const int MaxLongText = 20000;

    /// <summary>
    /// Stat columns are nullable and stay that way. A blank field means "the article does not
    /// say", which is different from zero and is the value most rows carry.
    /// </summary>
    public static IRuleBuilderOptions<T, int?> OptionalCount<T>(this IRuleBuilder<T, int?> rule) =>
        rule.GreaterThanOrEqualTo(0).WithMessage("Cannot be negative.");

    public static IRuleBuilderOptions<T, string?> OptionalUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(url => string.IsNullOrWhiteSpace(url) ||
                         (Uri.TryCreate(url, UriKind.Absolute, out var u) &&
                          (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
            .WithMessage("Must be an absolute http(s) URL, or empty.");
}

public class UpdateCharacterValidator : Validator<UpdateCharacterRequest>
{
    public UpdateCharacterValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Fields).NotNull();

        RuleFor(x => x.Fields.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(EditRules.MaxShortText);

        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
        RuleFor(x => x.Fields.Popularity).InclusiveBetween(0, 100);
        RuleFor(x => x.Fields.WikiPageLength).OptionalCount();
        RuleFor(x => x.Fields.WikiBacklinks).OptionalCount();
        RuleFor(x => x.Fields.GameId).GreaterThan(0);
        RuleFor(x => x.Fields.ImageUrl).OptionalUrl();
        RuleFor(x => x.Fields.GeneratedImageUrl).OptionalUrl();
    }
}

public class UpdateMonsterValidator : Validator<UpdateMonsterRequest>
{
    public UpdateMonsterValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Fields).NotNull();

        RuleFor(x => x.Fields.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(EditRules.MaxShortText);

        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
        RuleFor(x => x.Fields.Popularity).InclusiveBetween(0, 100);
        RuleFor(x => x.Fields.WikiPageLength).OptionalCount();
        RuleFor(x => x.Fields.WikiBacklinks).OptionalCount();
        RuleFor(x => x.Fields.GameId).GreaterThan(0);
        RuleFor(x => x.Fields.ImageUrl).OptionalUrl();
        RuleFor(x => x.Fields.GeneratedImageUrl).OptionalUrl();

        // Battle stats feed the arena and climb builders, which have no defence against a
        // negative attack value beyond never being given one.
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

public class UpdateGameValidator : Validator<UpdateGameRequest>
{
    public UpdateGameValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Fields).NotNull();

        RuleFor(x => x.Fields.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(EditRules.MaxShortText);

        RuleFor(x => x.Fields.Platform)
            .NotEmpty().WithMessage("Platform is required.")
            .MaximumLength(EditRules.MaxShortText);

        RuleFor(x => x.Fields.ReleaseYear)
            .InclusiveBetween(1980, DateTime.UtcNow.Year + 5)
            .WithMessage("Release year looks wrong.");

        RuleFor(x => x.Fields.Description).MaximumLength(EditRules.MaxLongText);
    }
}
