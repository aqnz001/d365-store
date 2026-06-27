using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Sync;

/// <summary>
/// Registers the catalog-sync services. The caller must also register the resilient
/// HttpClients via AddExternalHttpClients (PartsPortal.Shared.Http) so the "shopify"
/// client (used by <see cref="ShopifySimCatalogSink"/>, the catalog-store HTTP sink — DR-005)
/// is available.
/// </summary>
public static class CatalogSyncServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CatalogSyncOptions>(configuration.GetSection(CatalogSyncOptions.SectionName));

        // Source selection (DR-005): embedded sample (default, dev/test) vs Azure SQL BYOD replica.
        var sourceMode = configuration.GetValue($"{CatalogSyncOptions.SectionName}:SourceMode", CatalogSourceMode.Sample);
        if (sourceMode == CatalogSourceMode.Sql)
        {
            services.AddSingleton<IByodCatalogSource, SqlByodCatalogSource>();
        }
        else
        {
            services.AddSingleton<IByodCatalogSource, SampleByodCatalogSource>();
        }

        // Sink: the catalog store via the resilient "shopify" HttpClient (base URL = the real store
        // in Phase 2; the name is the DR-005 stand-in, renamed when the store is built).
        services.AddSingleton<IShopifyCatalogSink, ShopifySimCatalogSink>();
        services.AddSingleton<CatalogSyncJob>();
        return services;
    }
}
