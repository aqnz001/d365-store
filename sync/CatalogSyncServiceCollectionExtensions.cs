using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Sync;

/// <summary>
/// Registers the catalog-sync services. The caller must also register the resilient
/// HttpClients via AddExternalHttpClients (PartsPortal.Shared.Http) so the "shopify"
/// client (used by <see cref="ShopifySimCatalogSink"/>) is available.
/// </summary>
public static class CatalogSyncServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogSync(this IServiceCollection services)
    {
        services.AddSingleton<IByodCatalogSource, SampleByodCatalogSource>();
        services.AddSingleton<IShopifyCatalogSink, ShopifySimCatalogSink>();
        services.AddSingleton<CatalogSyncJob>();
        return services;
    }
}
