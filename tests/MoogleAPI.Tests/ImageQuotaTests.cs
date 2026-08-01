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
