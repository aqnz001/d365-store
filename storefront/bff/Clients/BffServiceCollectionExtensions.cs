using Microsoft.Extensions.Options;
using PartsPortal.Shared.Http;

namespace PartsPortal.Bff.Clients;

/// <summary>Named HttpClient identifiers for the BFF's downstream dependencies.</summary>
public static class BffClients
{
    public const string Catalog = "catalog";
    public const string Middleware = "middleware";
}

/// <summary>
/// Registers the BFF's resilient, config-driven HttpClients (catalog + middleware) and the
/// typed API clients. Reuses the integration layer's resilience policy (T4).
/// </summary>
public static class BffServiceCollectionExtensions
{
    public static IServiceCollection AddBffClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BffOptions>(configuration.GetSection(BffOptions.SectionName));

        AddResilientClient(services, BffClients.Catalog, o => o.CatalogBaseUrl);
        AddResilientClient(services, BffClients.Middleware, o => o.MiddlewareBaseUrl);

        services.AddScoped<ICatalogApi, CatalogApi>();
        services.AddScoped<IMiddlewareApi, MiddlewareApi>();
        return services;
    }

    private static void AddResilientClient(IServiceCollection services, string name, Func<BffOptions, string> baseUrl)
    {
        services.AddHttpClient(name)
            .ConfigureHttpClient((sp, client) =>
            {
                var url = baseUrl(sp.GetRequiredService<IOptions<BffOptions>>().Value);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    client.BaseAddress = new Uri(url);
                }
            })
            .AddResilienceHandler($"{name}-resilience", builder =>
                ResilientHttpClientExtensions.ConfigureHttpResilience(builder, new ResilienceOptions()));
    }
}
