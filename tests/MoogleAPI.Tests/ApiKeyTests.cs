using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MoogleAPI.Web.Infrastructure.RateLimiting;

namespace MoogleAPI.Tests;

public class ApiKeyTests
{
    private static ApiKeyValidator Validator(params string[] keys) =>
        new(Options.Create(new PremiumKeyOptions { Keys = [.. keys] }));

    private static HttpRequest RequestWith(string? apiKey)
    {
        var context = new DefaultHttpContext();
        if (apiKey is not null)
            context.Request.Headers[ApiKeyValidator.HeaderName] = apiKey;
        return context.Request;
    }

    [Fact]
    public void RecognizedKeyIsValid()
    {
        Assert.True(Validator("sponsor-key").IsValid("sponsor-key"));
    }

    // The regression this whole class exists for: any non-empty header used to buy the
    // premium limit, because the value was never compared against anything.
    [Theory]
    [InlineData("x")]
    [InlineData("not-a-real-key")]
    [InlineData("SPONSOR-KEY")]      // keys are case-sensitive
    [InlineData("sponsor-key-2")]    // no prefix matching
    public void UnrecognizedKeyIsNotValid(string apiKey)
    {
        Assert.False(Validator("sponsor-key").IsValid(apiKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankKeyIsNotValid(string? apiKey)
    {
        Assert.False(Validator("sponsor-key").IsValid(apiKey));
    }

    [Fact]
    public void NoConfiguredKeysMeansNobodyIsPremium()
    {
        var validator = Validator();

        Assert.False(validator.IsValid("sponsor-key"));
        Assert.False(validator.IsValid("anything"));
    }

    [Fact]
    public void SurroundingWhitespaceIsToleratedOnBothSides()
    {
        // Config values and curl invocations both pick up stray whitespace; a key that is
        // otherwise correct shouldn't be rejected for it.
        Assert.True(Validator("  sponsor-key  ").IsValid("sponsor-key"));
        Assert.True(Validator("sponsor-key").IsValid(" sponsor-key "));
    }

    [Fact]
    public void BlankConfiguredKeysAreDiscarded()
    {
        // An unset env var binds as an empty string. If that were kept as a key, sending an
        // empty header would match it and premium would be self-service again.
        var validator = Validator("", "   ", "sponsor-key");

        Assert.False(validator.IsValid(""));
        Assert.False(validator.IsValid("   "));
        Assert.True(validator.IsValid("sponsor-key"));
    }

    [Fact]
    public void ResolveKeyReturnsTheKeyForARecognizedHeader()
    {
        Assert.Equal("sponsor-key", Validator("sponsor-key").ResolveKey(RequestWith("sponsor-key")));
    }

    [Fact]
    public void ResolveKeyFallsBackToAnonymousRatherThanFailing()
    {
        var validator = Validator("sponsor-key");

        Assert.Null(validator.ResolveKey(RequestWith("bogus")));
        Assert.Null(validator.ResolveKey(RequestWith(null)));
    }

    [Fact]
    public void ResolveKeyNormalizesSoOneKeyCannotHoldSeveralRateLimitWindows()
    {
        // The resolved value becomes the limiter's partition key. If padding survived here,
        // " key" and "key " would be separate partitions and the limit would multiply.
        var validator = Validator("sponsor-key");

        Assert.Equal("sponsor-key", validator.ResolveKey(RequestWith(" sponsor-key")));
        Assert.Equal("sponsor-key", validator.ResolveKey(RequestWith("sponsor-key ")));
    }
}
