using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Features.Dashboard.Browse;
using MoogleAPI.Web.Features.Dashboard.Update;
using MoogleAPI.Web.Infrastructure.Data;
using MoogleAPI.Web.Infrastructure.Models;

namespace MoogleAPI.Web.Features.Dashboard.Create;

/// <summary>
/// Adds a character by hand.
/// </summary>
/// <remarks>
/// A name has to be unique within its game — the schema says so, with a unique index on
/// (Name, GameId) — so the duplicate is caught here and reported as a conflict naming the row
/// you already have. Letting the insert reach the database instead would answer a careful piece
/// of data entry with a 500 and a stack trace.
/// </remarks>
public class CharacterEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<CreateCharacterRequest, CreateResponse<CharacterRow>>
{
    public override void Configure()
    {
        Post("/dashboard/characters");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(CreateCharacterRequest req, CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == req.Fields.GameId, ct);
        if (game is null)
        {
            AddError(r => r.Fields.GameId, $"No game with id {req.Fields.GameId}.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var f = req.Fields;
        var character = new Character
        {
            Name = f.Name.Trim(),
            Description = EditText.Clean(f.Description),
            Role = EditText.Clean(f.Role),
            Affiliation = EditText.Clean(f.Affiliation),
            Race = EditText.Clean(f.Race),
            Hometown = EditText.Clean(f.Hometown),
            Job = EditText.Clean(f.Job),
            Weapon = EditText.Clean(f.Weapon),
            Abilities = EditText.Clean(f.Abilities),
            IsPlayable = f.IsPlayable,
            Popularity = f.Popularity,
            WikiPageLength = f.WikiPageLength,
            WikiBacklinks = f.WikiBacklinks,
            // A new character starts on whatever the import found, if anything — but the moment it
            // has house-style artwork that is what it serves, matching the update endpoint.
            ImageUrl = EditText.Clean(f.GeneratedImageUrl) ?? EditText.Clean(f.ImageUrl),
            ImageSourceUrl = EditText.Clean(f.ImageSourceUrl),
            GeneratedImageUrl = EditText.Clean(f.GeneratedImageUrl),
            ImageKind = EditText.Clean(f.ImageKind),
            GameId = game.Id,
        };

        var clash = await db.Characters
            .FirstOrDefaultAsync(c => c.GameId == game.Id && c.Name.ToLower() == character.Name.ToLower(), ct);

        if (clash is not null)
        {
            AddError(r => r.Fields.Name,
                $"{game.Name} already has a character called {clash.Name} (#{clash.Id}).");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        db.Characters.Add(character);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new CreateResponse<CharacterRow>(
            new CharacterRow(character.Id, game.Name, game.ReleaseYear, new CharacterEdit(
                character.Name, character.Description, character.Role, character.Affiliation,
                character.Race, character.Hometown, character.Job, character.Weapon,
                character.Abilities, character.IsPlayable, character.Popularity,
                character.WikiPageLength, character.WikiBacklinks, character.ImageUrl,
                character.ImageSourceUrl, character.GeneratedImageUrl, character.ImageKind,
                character.GameId)), null), ct);
    }
}

/// <summary>Adds a monster by hand. Same unique-name rule as characters.</summary>
public class MonsterEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<CreateMonsterRequest, CreateResponse<MonsterRow>>
{
    public override void Configure()
    {
        Post("/dashboard/monsters");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(CreateMonsterRequest req, CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == req.Fields.GameId, ct);
        if (game is null)
        {
            AddError(r => r.Fields.GameId, $"No game with id {req.Fields.GameId}.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var f = req.Fields;
        var monster = new Monster
        {
            Name = f.Name.Trim(),
            Description = EditText.Clean(f.Description),
            Category = EditText.Clean(f.Category),
            Location = EditText.Clean(f.Location),
            HitPoints = f.HitPoints,
            MagicPoints = f.MagicPoints,
            Level = f.Level,
            Experience = f.Experience,
            Gil = f.Gil,
            Attack = f.Attack,
            Defense = f.Defense,
            MagicAttack = f.MagicAttack,
            MagicDefense = f.MagicDefense,
            Speed = f.Speed,
            Evasion = f.Evasion,
            Abilities = EditText.Clean(f.Abilities),
            Drops = EditText.Clean(f.Drops),
            Steals = EditText.Clean(f.Steals),
            Weaknesses = EditText.Clean(f.Weaknesses),
            Absorbs = EditText.Clean(f.Absorbs),
            Popularity = f.Popularity,
            WikiPageLength = f.WikiPageLength,
            WikiBacklinks = f.WikiBacklinks,
            ImageUrl = EditText.Clean(f.ImageUrl),
            ImageSourceUrl = EditText.Clean(f.ImageSourceUrl),
            GeneratedImageUrl = EditText.Clean(f.GeneratedImageUrl),
            ImageKind = EditText.Clean(f.ImageKind),
            GameId = game.Id,
        };

        var clash = await db.Monsters
            .FirstOrDefaultAsync(m => m.GameId == game.Id && m.Name.ToLower() == monster.Name.ToLower(), ct);

        if (clash is not null)
        {
            AddError(r => r.Fields.Name,
                $"{game.Name} already has a monster called {clash.Name} (#{clash.Id}).");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        db.Monsters.Add(monster);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new CreateResponse<MonsterRow>(
            new MonsterRow(monster.Id, game.Name, game.ReleaseYear, new MonsterEdit(
                monster.Name, monster.Description, monster.Category, monster.Location,
                monster.HitPoints, monster.MagicPoints, monster.Level, monster.Experience,
                monster.Gil, monster.Attack, monster.Defense, monster.MagicAttack,
                monster.MagicDefense, monster.Speed, monster.Evasion, monster.Abilities,
                monster.Drops, monster.Steals, monster.Weaknesses, monster.Absorbs,
                monster.Popularity, monster.WikiPageLength, monster.WikiBacklinks,
                monster.ImageUrl, monster.ImageSourceUrl, monster.GeneratedImageUrl,
                monster.ImageKind, monster.GameId)), null), ct);
    }
}

/// <summary>
/// Adds a game. This is the one that used to need a scrape: a new game meant seeding the row,
/// then pulling its whole roster. Now it is a form, and the roster arrives a page at a time.
/// <para>
/// Unlike characters and monsters, game names carry no unique index, so a repeat is allowed and
/// only worth mentioning — a remake and its original are two rows that share almost everything.
/// </para>
/// </summary>
public class GameEndpoint(AppDbContext db, HybridCache cache)
    : Endpoint<CreateGameRequest, CreateResponse<GameRow>>
{
    public override void Configure()
    {
        Post("/dashboard/games");
        Policies("Dashboard");
        Description(b => b.ExcludeFromDescription());
        Options(x => x.DisableRateLimiting());
    }

    public override async Task HandleAsync(CreateGameRequest req, CancellationToken ct)
    {
        var f = req.Fields;
        var game = new Game
        {
            Name = f.Name.Trim(),
            ReleaseYear = f.ReleaseYear,
            Platform = f.Platform.Trim(),
            Description = EditText.Clean(f.Description),
        };

        var duplicate = await db.Games.AnyAsync(g => g.Name.ToLower() == game.Name.ToLower(), ct);

        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
        await CatalogCache.InvalidateAsync(cache, ct);

        await Send.OkAsync(new CreateResponse<GameRow>(
            new GameRow(game.Id, 0, 0, 0,
                new GameEdit(game.Name, game.ReleaseYear, game.Platform, game.Description,
                             game.ImageUrl, game.ThumbnailUrl, game.ImageSourceUrl)),
            duplicate ? $"There was already a game called {game.Name}." : null), ct);
    }
}
