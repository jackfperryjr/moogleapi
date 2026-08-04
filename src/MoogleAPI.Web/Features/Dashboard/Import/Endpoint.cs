using FastEndpoints;
using FluentValidation;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Infrastructure.Wiki;

namespace MoogleAPI.Web.Features.Dashboard.Import;

public class ImportRequest
{
    /// <summary>A wiki article URL, or just its title.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>"characters" or "monsters" — which parser to read the page with.</summary>
    public string Resource { get; set; } = "characters";

    /// <summary>The game the draft row would belong to.</summary>
    public int GameId { get; set; }
}

/// <param name="Character">Filled for a character import, null otherwise.</param>
/// <param name="Monster">Filled for a monster import, null otherwise.</param>
/// <param name="Notes">
/// What the parse could not do, in plain words — an empty infobox, a page that looks like an
/// index rather than a creature. Never fatal: a thin draft you finish by hand still beats
/// typing twenty fields from scratch.
/// </param>
public record ImportResponse(
    string Title,
    string SourceUrl,
    CharacterEdit? Character,
    MonsterEdit? Monster,
    List<string> Notes
);

public class ImportValidator : Validator<ImportRequest>
{
    public ImportValidator()
    {
        RuleFor(x => x.Page).NotEmpty().WithMessage("Paste a wiki URL or article title.");
        RuleFor(x => x.GameId).GreaterThan(0).WithMessage("Pick a game for the imported row.");
        RuleFor(x => x.Resource)
            .Must(r => r is "characters" or "monsters")
            .WithMessage("Resource must be characters or monsters.");
    }
}

/// <summary>
/// Reads one Final Fantasy Wiki article and returns it as an unsaved draft row.
/// </summary>
/// <remarks>
/// <para>
/// This is what is left of the scraper, and the difference is the whole point of it. The bulk
/// stages decided for themselves which pages existed, matched them to rows by name, and wrote
/// the results straight to the database — which is why they could not be run again once the data
/// had been curated. This fetches the one page you asked for, parses it, and hands it back. It
/// writes nothing. What you do with the draft is a separate request to the create endpoint,
/// after you have looked at every field.
/// </para>
/// <para>
/// The parsers are the same ones that built the catalogue, tests and all — the wiki's infobox
/// layout has not changed, only our relationship to it.
/// </para>
/// </remarks>
public class Endpoint(WikiClient wiki) : Endpoint<ImportRequest, ImportResponse>
{
    public override void Configure()
    {
        Post("/dashboard/import");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(ImportRequest req, CancellationToken ct)
    {
        var title = TitleFrom(req.Page);
        if (title.Length == 0)
        {
            AddError(r => r.Page, "That does not look like a wiki article.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var notes = new List<string>();
        var sourceUrl = $"https://finalfantasy.fandom.com/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";

        if (req.Resource == "characters")
        {
            var details = await wiki.GetCharacterDetailsAsync(title, ct);
            var signals = await wiki.GetPageSignalsAsync(title, ct);

            if (signals is null)
                notes.Add("No page found by that name — check the spelling, or the draft will be empty.");
            if (details.Description is null && details.Role is null && details.ImageUrl is null)
                notes.Add("Nothing parsed out of the article: it may be a redirect or a disambiguation page.");

            await Send.OkAsync(new ImportResponse(title, sourceUrl, new CharacterEdit(
                Name: WikiText.RepairName(WikiText.NormalizeName(title)),
                Description: WikiText.Clean(details.Description),
                Role: WikiText.Clean(details.Role),
                Affiliation: WikiText.Clean(details.Affiliation),
                Race: WikiText.Clean(details.Race),
                Hometown: WikiText.Clean(details.Hometown),
                Job: WikiText.Clean(details.Job),
                Weapon: WikiText.Clean(details.Weapon),
                Abilities: WikiText.Clean(details.Abilities),
                // Not something a page states, and the navbox that used to answer it covered a
                // whole cast at once. One page cannot, so it starts false and you set it.
                IsPlayable: false,
                Popularity: WikiScoring.ScorePopularity(signals),
                WikiPageLength: signals?.PageLength,
                WikiBacklinks: signals?.Backlinks,
                // The wiki's own URL, left for you to keep or replace by uploading a file. It is
                // put in ImageUrl rather than fetched here so that nothing is copied into the
                // bucket on a draft you might discard.
                ImageUrl: details.ImageUrl,
                ImageSourceUrl: null,
                GeneratedImageUrl: null,
                ImageKind: null,
                GameId: req.GameId), null, notes), ct);
            return;
        }

        var monster = await wiki.GetMonsterDetailsAsync(title, ct);

        if (WikiText.IsNotAMonster(title) || WikiScoring.IsMetaArticle(title))
            notes.Add("That title reads like an index or reference page rather than one enemy.");
        if (monster.Stats == MonsterStats.Empty)
            notes.Add("No stats infobox found — the battle stats are all blank.");

        var s = monster.Stats;
        await Send.OkAsync(new ImportResponse(title, sourceUrl, null, new MonsterEdit(
            Name: WikiText.RepairMonsterName(WikiText.NormalizeName(title)),
            Description: WikiText.Clean(monster.Description),
            Category: WikiText.Clean(monster.Type),
            Location: WikiText.Clean(monster.Location),
            HitPoints: s.HitPoints,
            MagicPoints: s.MagicPoints,
            Level: s.Level,
            Experience: s.Experience,
            Gil: s.Gil,
            Attack: s.Attack,
            Defense: s.Defense,
            MagicAttack: s.MagicAttack,
            MagicDefense: s.MagicDefense,
            Speed: s.Speed,
            Evasion: s.Evasion,
            Abilities: WikiText.Clean(s.Abilities),
            Drops: WikiText.Clean(s.Drops),
            Steals: WikiText.Clean(s.Steals),
            Weaknesses: WikiText.Clean(s.Weaknesses),
            Absorbs: WikiText.Clean(s.Absorbs),
            Popularity: WikiScoring.ScorePopularity(monster.Signals),
            WikiPageLength: monster.Signals?.PageLength,
            WikiBacklinks: monster.Signals?.Backlinks,
            ImageUrl: monster.ImageUrl,
            ImageSourceUrl: null,
            GeneratedImageUrl: null,
            ImageKind: null,
            GameId: req.GameId), notes), ct);
    }

    /// <summary>
    /// Accepts what you would actually paste: a full article URL, or the title on its own.
    /// Underscores become spaces, because that is how the URL spells a title and not how the
    /// API wants one.
    /// </summary>
    internal static string TitleFrom(string page)
    {
        var text = page.Trim();
        if (text.Length == 0) return "";

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var last = segments.LastOrDefault();
            if (last is null) return "";
            text = Uri.UnescapeDataString(last);
        }

        return text.Replace('_', ' ').Trim();
    }
}
