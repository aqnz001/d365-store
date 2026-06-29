using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace PartsPortal.Bff.Security;

/// <summary>
/// BFF hardening (prod-readiness #6): rate limiting, structured request logging, and response
/// security headers (incl. CSP). All values are config-driven; sensible defaults apply when unset.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>Name of the stricter rate-limit policy applied to auth + payment endpoints.</summary>
    public const string SensitivePolicy = "sensitive";

    public static IServiceCollection AddBffSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Forwarded headers ----------------------------------------------------------------
        // So the rate limiter's IP fallback reflects the real client behind APIM/Front Door, not the
        // proxy. Only the configured proxy networks are trusted (empty = loopback only), so an
        // untrusted client cannot spoof X-Forwarded-For to evade or poison the IP partition.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
            foreach (var entry in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                var parts = entry.Split('/', StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var length))
                {
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
                }
            }
        });

        // ---- Rate limiting --------------------------------------------------------------------
        // A global per-client window (keyed by authenticated customer, else remote IP) plus a much
        // stricter policy for credential/payment endpoints. Config: RateLimit:PermitPerMinute (global)
        // and RateLimit:SensitivePerMinute (auth/pay).
        var permitPerMinute = configuration.GetValue("RateLimit:PermitPerMinute", 300);
        var sensitivePerMinute = configuration.GetValue("RateLimit:SensitivePerMinute", 20);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(SensitivePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = sensitivePerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        // ---- Structured request logging -------------------------------------------------------
        // The BFF previously emitted no request telemetry. Log method/path/status/duration; never
        // request/response bodies and never the auth cookie or dev-customer header (no PII in logs).
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        return services;
    }

    /// <summary>
    /// Adds response security headers (incl. a Stripe/Google-Fonts-aware CSP). The CSP can be
    /// overridden wholesale via Security:ContentSecurityPolicy.
    /// </summary>
    public static IApplicationBuilder UseBffSecurityHeaders(this WebApplication app)
    {
        var csp = app.Configuration["Security:ContentSecurityPolicy"] ?? DefaultCsp;

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = csp;
            await next();
        });
    }

    // Partition by the authenticated customer when present, else the remote IP — so one client
    // can't exhaust another's budget and unauthenticated floods are still bounded.
    private static string ClientKey(HttpContext context) =>
        context.User.FindFirst(Auth.DevAuthenticationHandler.CustomerClaim)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";

    // self everywhere; Stripe.js + its iframes/API for SAQ-A card capture; Google Fonts for the
    // typeface; inline styles allowed (the SPA uses style props) but NOT inline scripts.
    private const string DefaultCsp =
        "default-src 'self'; " +
        "script-src 'self' https://js.stripe.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self' https://api.stripe.com; " +
        "frame-src https://js.stripe.com https://hooks.stripe.com; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'";
}
