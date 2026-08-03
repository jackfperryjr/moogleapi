using MoogleAPI.Scraper.Scrapers;

namespace MoogleAPI.Tests;

/// <summary>
/// A 429 from the Gemini API means either "too fast" or "come back tomorrow", and only the
/// response body distinguishes them. Getting it wrong is expensive in one direction: treating the
/// daily quota as a burst limit spends three requests per row discovering a wall that will not
/// move for hours.
/// </summary>
/// <remarks>
/// The daily-quota fixture is a verbatim capture from
/// <c>gemini-3.1-flash-lite-image</c> on 2026-08-01, so this test fails if the shape of the
/// quota payload drifts.
/// </remarks>
public class ImageQuotaTests
{
    private const string DailyQuotaBody = """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota, please check your plan and billing details.\n* Quota exceeded for metric: generativelanguage.googleapis.com/generate_requests_per_model_per_day, limit: 1000, model: gemini-3.1-flash-lite-image\nPlease retry in 20h3m13.257628674s.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "quotaMetric": "generativelanguage.googleapis.com/generate_requests_per_model_per_day",
                    "quotaId": "GenerateRequestsPerDayPerProjectPerModel",
                    "quotaDimensions": { "model": "gemini-3.1-flash-lite-image", "location": "global" },
                    "quotaValue": "1000"
                  }
                ]
              },
              {
                "@type": "type.googleapis.com/google.rpc.RetryInfo",
                "retryDelay": "72193s"
              }
            ]
          }
        }
        """;

    private const string PerMinuteBody = """
        {
          "error": {
            "code": 429,
            "message": "Resource has been exhausted (e.g. check quota).",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  {
                    "quotaMetric": "generativelanguage.googleapis.com/generate_requests_per_model_per_minute",
                    "quotaId": "GenerateRequestsPerMinutePerProjectPerModel",
                    "quotaDimensions": { "model": "gemini-3.1-flash-lite-image", "location": "global" },
                    "quotaValue": "10"
                  }
                ]
              }
            ]
          }
        }
        """;

    [Fact]
    public void RecognisesThePerDayQuota()
    {
        Assert.True(ImageGenerator.IsDailyQuota(DailyQuotaBody, out _));
    }

    [Fact]
    public void ReportsWhenThePerDayQuotaResets()
    {
        ImageGenerator.IsDailyQuota(DailyQuotaBody, out var detail);

        // 72193s is 20h 3m. Operators plan the next batch off this number.
        Assert.Equal("resets in about 20h 3m", detail);
    }

    [Fact]
    public void TreatsThePerMinuteLimitAsRetryable()
    {
        // Same status code and same RESOURCE_EXHAUSTED status — only quotaId separates them.
        Assert.False(ImageGenerator.IsDailyQuota(PerMinuteBody, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{\"error\":{\"code\":429}}")]
    [InlineData("{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}")]
    public void FallsBackToRetryingWhenTheBodyIsNotRecognisable(string body)
    {
        // Unrecognised must mean "retry": a wrong guess of "burst" costs one wasted attempt,
        // a wrong guess of "daily" abandons a run that could have finished.
        Assert.False(ImageGenerator.IsDailyQuota(body, out _));
    }
}

/// <summary>
/// A depleted prepaid balance arrives as 429 RESOURCE_EXHAUSTED, exactly like throttling, but
/// no amount of backing off will ever clear it.
/// </summary>
/// <remarks>
/// The fixture is a verbatim capture from <c>gemini-3.1-flash-lite-image</c> on 2026-08-03,
/// taken while a 400-image batch was failing every row. Read as an ordinary burst limit, it cost
/// the whole six-hour job timeout and produced no images and no diagnosis — the account had
/// simply run out of money. Note it carries no <c>details</c> array at all, which is why it
/// cannot be matched the way the per-day quota is.
/// </remarks>
public class CreditExhaustionTests
{
    private const string DepletedCreditsBody = """
        {
          "error": {
            "code": 429,
            "message": "Your prepayment credits are depleted. Please go to AI Studio at https://ai.studio/projects to manage your project and billing. Learn more at https://ai.google.dev/gemini-api/docs/billing#prepay. ",
            "status": "RESOURCE_EXHAUSTED"
          }
        }
        """;

    private const string PerMinuteBody = """
        {
          "error": {
            "code": 429,
            "message": "Resource has been exhausted (e.g. check quota).",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [ { "quotaId": "GenerateRequestsPerMinutePerProjectPerModel" } ]
              }
            ]
          }
        }
        """;

    [Fact]
    public void RecognisesADepletedPrepaidBalance()
    {
        Assert.True(ImageGenerator.IsCreditsExhausted(DepletedCreditsBody));
    }

    [Fact]
    public void ClassifiesDepletedCreditsAsFatalRatherThanThrottling()
    {
        var kind = ImageGenerator.ClassifyRefusal(DepletedCreditsBody, out var detail);

        Assert.Equal(RefusalKind.CreditsExhausted, kind);
        Assert.Contains("prepayment credits are depleted", detail);
    }

    [Fact]
    public void DoesNotMistakeABurstLimitForDepletedCredits()
    {
        // The expensive direction is the other one, but this way round would abandon a run that
        // only needed to wait twenty seconds.
        Assert.False(ImageGenerator.IsCreditsExhausted(PerMinuteBody));
        Assert.Equal(RefusalKind.Burst, ImageGenerator.ClassifyRefusal(PerMinuteBody, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{\"error\":{\"code\":429}}")]
    public void TreatsAnUnreadableBodyAsRetryable(string body)
    {
        Assert.False(ImageGenerator.IsCreditsExhausted(body));
        Assert.Equal(RefusalKind.Burst, ImageGenerator.ClassifyRefusal(body, out _));
    }
}

/// <summary>
/// Bodies are verbatim shapes of Gemini 429 responses, so these fail if the error envelope
/// changes the field the backoff now depends on.
/// </summary>
public class RetryDelayTests
{
    private const string BurstLimitBody = """
        {
          "error": {
            "code": 429,
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              {
                "@type": "type.googleapis.com/google.rpc.QuotaFailure",
                "violations": [
                  { "quotaId": "GenerateRequestsPerMinutePerProjectPerModel" }
                ]
              },
              { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "43s" }
            ]
          }
        }
        """;

    /// <summary>
    /// The bug this exists for: the API says how long it wants us gone, and that value was read
    /// only to phrase the daily-quota message and thrown away for burst limits. The wait was a
    /// guessed ladder, and a guess that came back early spends a request to be told the same
    /// thing again — 225 images in one batch were abandoned that way.
    /// </summary>
    [Fact]
    public void ReadsTheDelayTheApiAsksFor() =>
        Assert.Equal(TimeSpan.FromSeconds(43), ImageGenerator.RetryDelayFrom(BurstLimitBody));

    [Fact]
    public void ABurstLimitIsNotTheDailyQuota() =>
        Assert.False(ImageGenerator.IsDailyQuota(BurstLimitBody, out _));

    [Theory]
    [InlineData("""{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[]}}""")]
    [InlineData("""{"error":{"code":500}}""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void FallsBackToTheLadderWhenNoDelayIsGiven(string body) =>
        Assert.Null(ImageGenerator.RetryDelayFrom(body));

    /// <summary>A zero or negative hint is not a wait, and must not shorten the ladder to nothing.</summary>
    [Theory]
    [InlineData("0s")]
    [InlineData("-5s")]
    public void IgnoresAnUnusableDelay(string raw)
    {
        var body = """{"error":{"code":429,"details":[{"retryDelay":"RAW"}]}}""".Replace("RAW", raw);

        Assert.Null(ImageGenerator.RetryDelayFrom(body));
    }
}
