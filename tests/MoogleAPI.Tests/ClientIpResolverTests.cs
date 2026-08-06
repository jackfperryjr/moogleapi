using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MoogleAPI.Web.Infrastructure.RateLimiting;
using System.Net;

namespace MoogleAPI.Tests;

/// <summary>
/// The resolver decides which rate-limit bucket a caller lands in, so the interesting cases are
/// all the ones where a caller would like to choose that for themselves.
/// </summary>
public class ClientIpResolverTests
{
    private const string Secret = "edge-secret-value";
    private const string PeerAddress = "100.64.0.5";   // Railway's load balancer, as seen in production
    private const string CallerAddress = "203.0.113.9";

    private static ClientIpResolver Resolver(string? secret = Secret) =>
        new(Options.Create(new EdgeOptions { Secret = secret }));

    private static HttpRequest Request(string? edgeSecret, string? forwardedFor, string? peer = PeerAddress)
    {
        var context = new DefaultHttpContext();

        if (peer is not null)
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);

        if (edgeSecret is not null)
            context.Request.Headers[ClientIpResolver.SecretHeaderName] = edgeSecret;

        if (forwardedFor is not null)
            context.Request.Headers[ClientIpResolver.ClientIpHeaderName] = forwardedFor;

        return context.Request;
    }

    [Fact]
    public void TrustsTheForwardedAddressWhenTheEdgeSecretMatches()
    {
        var resolved = Resolver().Resolve(Request(Secret, CallerAddress));

        Assert.Equal(CallerAddress, resolved);
    }

    [Fact]
    public void IgnoresTheForwardedAddressWhenTheSecretIsWrong()
    {
        var resolved = Resolver().Resolve(Request("not-the-secret", CallerAddress));

        Assert.Equal(PeerAddress, resolved);
    }

    [Fact]
    public void IgnoresTheForwardedAddressWhenNoSecretIsPresented()
    {
        // The bypass that matters: Railway answers on its own hostname too, so a caller who could
        // set CF-Connecting-IP by hand there would be picking their own rate-limit partition.
        var resolved = Resolver().Resolve(Request(edgeSecret: null, forwardedFor: CallerAddress));

        Assert.Equal(PeerAddress, resolved);
    }

    [Fact]
    public void IgnoresForwardedAddressesEntirelyWhenNoSecretIsConfigured()
    {
        // Local runs and any deploy that hasn't been given the secret: believe nothing forwarded.
        var resolved = Resolver(secret: null).Resolve(Request(Secret, CallerAddress));

        Assert.Equal(PeerAddress, resolved);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("203.0.113.9, 198.51.100.4")]   // a header holding a list, not one address
    public void FallsBackWhenTheForwardedValueIsNotAnAddress(string forwarded)
    {
        // Unparsed, this would become a partition key of its own, so a caller sending junk could
        // mint a fresh window per request.
        var resolved = Resolver().Resolve(Request(Secret, forwarded));

        Assert.Equal(PeerAddress, resolved);
    }

    [Fact]
    public void ReturnsUnknownRatherThanNullWhenThereIsNoAddressAtAll()
    {
        // Null would collapse every such request into one bucket keyed on "ip:".
        var resolved = Resolver().Resolve(Request(edgeSecret: null, forwardedFor: null, peer: null));

        Assert.Equal("unknown", resolved);
    }

    [Fact]
    public void NormalisesTheForwardedAddress()
    {
        // Parsed and re-rendered, so a padded value and a clean one cannot become two buckets.
        var resolved = Resolver().Resolve(Request(Secret, " 203.0.113.9 "));

        Assert.Equal(CallerAddress, resolved);
    }
}
