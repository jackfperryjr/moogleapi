using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace MoogleAPI.Web.Infrastructure.RateLimiting;

/// <summary>
/// Works out who is actually calling, which <c>HttpContext.Connection.RemoteIpAddress</c> does not.
/// </summary>
/// <remarks>
/// Railway terminates the connection at its own load balancer, so the peer address Kestrel sees is
/// one of a small pool of internal <c>100.64.0.0/10</c> addresses — measured against three months of
/// production logs on 2026-08-05, the ten busiest "clients" were <c>100.64.0.3</c> through
/// <c>100.64.0.9</c>, and the distinct-address count per day sat at 21–24 whether the day served 56
/// requests or 1,730. Every caller was therefore sharing about twenty rate-limit buckets with every
/// other caller, and the request log's <c>IpHash</c> was recording infrastructure.
/// <para>
/// The fix cannot simply be <c>X-Forwarded-For</c>. Cloudflare <em>appends</em> to whatever value the
/// caller sends, so its leftmost entry is attacker-controlled, and partitioning a rate limiter on a
/// value the attacker chooses is worse than partitioning on the load balancer: it would let one
/// client mint unlimited fresh windows. <c>CF-Connecting-IP</c> is written by Cloudflare and cannot
/// be forged through it — but Railway also answers on its own <c>*.up.railway.app</c> hostname, where
/// nothing stops a caller setting that header by hand.
/// </para>
/// <para>
/// So the header is trusted only when the request carries a secret that the edge Worker injects and
/// that a direct-to-origin caller cannot know. Without the secret configured, or on a request that
/// doesn't carry it, this falls back to the peer address — the old behaviour, which is imprecise but
/// never wrong in the attacker's favour.
/// </para>
/// </remarks>
public class ClientIpResolver(IOptions<EdgeOptions> options)
{
    /// <summary>Injected by cloudflare/maintenance-worker/worker.js on every proxied request.</summary>
    public const string SecretHeaderName = "X-Moogle-Edge";

    /// <summary>Set by Cloudflare to the true client address; forgeable only if the edge is bypassed.</summary>
    public const string ClientIpHeaderName = "CF-Connecting-IP";

    private readonly byte[]? _secret = string.IsNullOrWhiteSpace(options.Value.Secret)
        ? null
        : Encoding.UTF8.GetBytes(options.Value.Secret);

    /// <summary>
    /// The caller's address, or <c>"unknown"</c> when there isn't one. Never null, because it keys
    /// a rate-limit partition: a null would collapse every such request into one bucket.
    /// </summary>
    public string Resolve(HttpRequest request)
    {
        if (CameFromOurEdge(request))
        {
            // Trimmed like the API key header is: IPAddress.TryParse rejects surrounding
            // whitespace outright, and a stray space is not a reason to give up on the address.
            var forwarded = request.Headers[ClientIpHeaderName].ToString().Trim();

            // Parsed rather than taken as text. The value keys a partition and is hashed into the
            // request log, and a caller who could put arbitrary strings there could inflate both.
            if (System.Net.IPAddress.TryParse(forwarded, out var address))
                return address.ToString();
        }

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private bool CameFromOurEdge(HttpRequest request)
    {
        if (_secret is null)
            return false;

        var presented = request.Headers[SecretHeaderName].ToString();
        if (string.IsNullOrEmpty(presented))
            return false;

        // Fixed-time: the comparison is attacker-driven and runs on every request, which is the
        // shape a timing attack needs. FixedTimeEquals also handles the length mismatch safely.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), _secret);
    }
}

/// <summary>Configuration for the trusted edge. Supply as <c>Edge__Secret</c>.</summary>
public class EdgeOptions
{
    public const string SectionName = "Edge";

    /// <summary>
    /// Shared with the Cloudflare Worker, which sends it as <see cref="ClientIpResolver.SecretHeaderName"/>.
    /// Empty is a legitimate state — it means "don't trust forwarded addresses", which is what a
    /// local run wants — so this is deliberately not validated at startup.
    /// </summary>
    public string? Secret { get; set; }
}
