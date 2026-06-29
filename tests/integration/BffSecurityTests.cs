using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using PartsPortal.Bff;
using PartsPortal.Bff.Auth;
using Xunit;

namespace PartsPortal.Tests.Integration;

/// <summary>
/// Prod-readiness #6 — BFF hardening: every response carries security headers (incl. CSP), and the
/// sensitive (auth/payment) endpoints are rate limited.
/// </summary>
public class BffSecurityTests(WebApplicationFactory<BffApp> factory) : IClassFixture<WebApplicationFactory<BffApp>>
{
    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", string.Join("", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join("", response.Headers.GetValues("X-Frame-Options")));
        var csp = string.Join("", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("https://js.stripe.com", csp); // Stripe.js allowed for SAQ-A card capture
    }

    [Fact]
    public async Task Sensitive_endpoints_are_rate_limited()
    {
        // Tighten the sensitive limit to 2/min for the test, then exceed it on the login endpoint.
        var client = factory
            .WithWebHostBuilder(b => b.UseSetting("RateLimit:SensitivePerMinute", "2"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var first = await client.GetAsync("/api/auth/login");
        var second = await client.GetAsync("/api/auth/login");
        var third = await client.GetAsync("/api/auth/login");

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode); // 3rd request in the window is shed
    }

    [Fact]
    public async Task Rate_limit_buckets_are_partitioned_per_customer()
    {
        // Tight global limit of 2/min; the limiter runs after authentication so it keys by the
        // authenticated customer — one customer's traffic must not throttle another's.
        var client = factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:PermitPerMinute", "2")).CreateClient();

        async Task<HttpStatusCode> Hit(string customer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            request.Headers.Add(DevAuthenticationHandler.CustomerHeader, customer);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        // Customer A exhausts its own 2/min bucket.
        Assert.Equal(HttpStatusCode.OK, await Hit("C-A"));
        Assert.Equal(HttpStatusCode.OK, await Hit("C-A"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Hit("C-A"));
        // Customer B has an independent bucket — not throttled by A's traffic.
        Assert.Equal(HttpStatusCode.OK, await Hit("C-B"));
    }
}
