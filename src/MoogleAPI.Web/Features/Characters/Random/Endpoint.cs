using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MoogleAPI.Web.Infrastructure.Data;

namespace MoogleAPI.Web.Features.Characters.Random;

public class Endpoint(AppDbContext db, HybridCache cache) : Endpoint<RandomCharacterRequest, RandomCharacterResponse>
{
    public override void Configure()
    {
        Get("/characters/random");
        AllowAnonymous();
        Description(b => b
            .WithName("GetRandomCharacter")
            .WithSummary("Get a random character, optionally seeded for a deterministic daily pick")
            .WithTags("Characters"));
    }

    public override async Task HandleAsync(RandomCharacterRequest req, CancellationToken ct)
    {
        // A seeded pick is stable, so it can be cached. An unseeded pick must not be —
        // caching it would hand every caller the same "random" character until expiry.
        var response = req.Seed is { Length: > 0 } seed
            ? await cache.GetOrCreateAsync(
                $"characters:random:seed={seed}:game={req.GameId}:pop={req.MinPopularity}:img={req.RequireImage}",
                async token => await PickAsync(req, seed, token),
                cancellationToken: ct)
            : await PickAsync(req, null, ct);

        if (response is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(response, ct);
    }

    private async Task<RandomCharacterResponse?> PickAsync(RandomCharacterRequest req, string? seed, CancellationToken ct)
    {
        var query = db.Characters.Include(c => c.Game).AsQueryable();

        if (req.GameId.HasValue)
            query = query.Where(c => c.GameId == req.GameId.Value);

        if (req.MinPopularity > 0)
            query = query.Where(c => c.Popularity >= req.MinPopularity);

        if (req.RequireImage)
            query = query.Where(c => c.ImageUrl != null);

        var total = await query.CountAsync(ct);
        if (total == 0) return null;

        var offset = seed is null
            ? System.Random.Shared.Next(total)
            : (int)(StableHash(seed) % (ulong)total);

        // Ordering must be deterministic for a seeded pick to be reproducible.
        return await query
            .OrderBy(c => c.Id)
            .Skip(offset)
            .Take(1)
            .Select(c => new RandomCharacterResponse(
                c.Id, c.Name, c.Description, c.Role, c.Affiliation, c.Race,
                c.Hometown, c.ImageUrl, c.Game.Name, c.Game.ReleaseYear, c.Popularity))
            .FirstOrDefaultAsync(ct);
    }

    // string.GetHashCode is randomized per process, so a daily seed would resolve to a
    // different character on every restart and across replicas. SHA-256 is stable forever.
    private static ulong StableHash(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return BitConverter.ToUInt64(hash, 0);
    }
}
