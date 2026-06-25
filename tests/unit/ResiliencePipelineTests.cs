using System.Net;
using PartsPortal.Shared.Http;
using Polly;
using Xunit;

namespace PartsPortal.Tests.Unit;

/// <summary>
/// T4 — covers the outbound-HTTP retry/backoff policy (TDD §9) by exercising the shared
/// resilience pipeline directly. Base delay is set to ~0 so the tests stay fast.
/// </summary>
public class ResiliencePipelineTests
{
    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(int maxRetries)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        ResilientHttpClientExtensions.ConfigureHttpResilience(
            builder,
            new ResilienceOptions { MaxRetryAttempts = maxRetries, BaseDelayMilliseconds = 1, TimeoutSeconds = 30 });
        return builder.Build();
    }

    [Fact]
    public async Task Transient_5xx_is_retried_then_succeeds()
    {
        var pipeline = BuildPipeline(maxRetries: 3);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            var code = attempts < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return ValueTask.FromResult(new HttpResponseMessage(code));
        });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(3, attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Persistent_transient_failure_exhausts_bounded_retries()
    {
        var pipeline = BuildPipeline(maxRetries: 3);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(4, attempts); // 1 initial + 3 bounded retries, then gives up
    }

    [Fact]
    public async Task Success_on_first_attempt_does_not_retry()
    {
        var pipeline = BuildPipeline(maxRetries: 3);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, attempts);
    }
}
