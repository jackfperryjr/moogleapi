using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Dashboard.Update;

/// <summary>
/// Writes one hand-edited character back.
/// </summary>
/// <remarks>
/// Last write wins — there is no row version and no conflict detection, because the policy on
/// this endpoint admits exactly one person and two tabs racing each other is not a problem worth
/// a column. What it does guard is the shape of the data the rest of the app relies on:
/// blank text becomes null rather than an empty string, and the game must exist.
/// </remarks>
public class CharacterEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<UpdateCharacterRequest, UpdateResponse<CharacterRow>>
{
    public override void Configure()
    {
        Put("/dashboard/characters/{id}");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(UpdateCharacterRequest req, CancellationToken ct)
    {
        var character = await db.Characters.FirstOrDefaultAsync(c => c.Id == req.Id, ct);
        if (character is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == req.Fields.GameId, ct);
        if (game is null)
        {
            AddError(r => r.Fields.GameId, $"No game with id {req.Fields.GameId}.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var f = req.Fields;
        var name = f.Name.Trim();

        // Renaming into a name the same game already uses would hit the unique index on
        // (Name, GameId) and come back as a 500. Curation renames rows constantly, so the
        // collision is reported as a conflict against the row that holds the name.
        var clash = await db.Characters
            .FirstOrDefaultAsync(x => x.Id != character.Id && x.GameId == game.Id &&
                                      x.Name.ToLower() == name.ToLower(), ct);

        if (clash is not null)
        {
            AddError(r => r.Fields.Name,
                $"{game.Name} already has a character called {clash.Name} (#{clash.Id}).");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        character.Name = name;
        character.Description = EditText.Clean(f.Description);
        character.Role = EditText.Clean(f.Role);
        character.Affiliation = EditText.Clean(f.Affiliation);
        character.Race = EditText.Clean(f.Race);
        character.Hometown = EditText.Clean(f.Hometown);
        character.Job = EditText.Clean(f.Job);
        character.Weapon = EditText.Clean(f.Weapon);
        character.Abilities = EditText.Clean(f.Abilities);
        character.IsPlayable = f.IsPlayable;
        character.Popularity = f.Popularity;
        character.WikiPageLength = f.WikiPageLength;
        character.WikiBacklinks = f.WikiBacklinks;
        character.ImageUrl = EditText.Clean(f.ImageUrl);
        character.ImageSourceUrl = EditText.Clean(f.ImageSourceUrl);
        character.GeneratedImageUrl = EditText.Clean(f.GeneratedImageUrl);
        character.ImageKind = EditText.Clean(f.ImageKind);
        character.GameId = game.Id;

        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new UpdateResponse<CharacterRow>(
            new CharacterRow(character.Id, game.Name, game.ReleaseYear, new CharacterEdit(
                character.Name, character.Description, character.Role, character.Affiliation,
                character.Race, character.Hometown, character.Job, character.Weapon,
                character.Abilities, character.IsPlayable, character.Popularity,
                character.WikiPageLength, character.WikiBacklinks, character.ImageUrl,
                character.ImageSourceUrl, character.GeneratedImageUrl, character.ImageKind,
                character.GameId))), ct);
    }
}
