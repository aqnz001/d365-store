using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace PartsPortal.Shared.Http;

/// <summary>
/// Registers resilient, config-driven HttpClients for the external systems (IVS, OData,
/// pricing/credit). Endpoints come from <see cref="ExternalEndpointOptions"/> (mocks now,
/// sandbox in Phase 2 — Golden Rule #1); retry/backoff/timeout from <see cref="ResilienceOptions"/>
/// (TDD §9). Functions call <see cref="AddExternalHttpClients"/> in their host setup (T6+).
/// </summary>
public static class ResilientHttpClientExtensions
{
    public const string IvsClient = "ivs";
    public const string ODataClient = "odata";
    public const string PricingCreditClient = "pricing-credit";
    public const string ShopifyClient = "shopify";

    public static IServiceCollection AddExternalHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExternalEndpointOptions>(configuration.GetSection(ExternalEndpointOptions.SectionName));
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));

        AddClient(services, IvsClient, o => o.IvsBaseUrl);
        AddClient(services, ODataClient, o => o.ODataBaseUrl);
        AddClient(services, PricingCreditClient, o => o.PricingCreditBaseUrl);
        AddClient(services, ShopifyClient, o => o.ShopifyBaseUrl);
        return services;
    }

    private static void AddClient(IServiceCollection services, string name, Func<ExternalEndpointOptions, string> baseUrl)
    {
        services.AddHttpClient(name)
            .ConfigureHttpClient((sp, client) =>
            {
                var url = baseUrl(sp.GetRequiredService<IOptions<ExternalEndpointOptions>>().Value);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    client.BaseAddress = new Uri(url);
                }
            })
            .AddResilienceHandler($"{name}-resilience", (builder, context) =>
                ConfigureHttpResilience(builder, context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value));
    }

    /// <summary>
    /// Applies the shared retry (exponential backoff + jitter, bounded) and timeout policy.
    /// Public so production wiring and tests exercise identical behavior. The default
    /// transient predicate handles 5xx / 408 / HttpRequestException / timeouts.
    /// </summary>
    public static void ConfigureHttpResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder, ResilienceOptions options)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(options.BaseDelayMilliseconds),
        });
        builder.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
    }
}
