using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Shared.Caching;

namespace PartsPortal.Shared.Status;

/// <summary>Registers the order-status mirror store + the status sync service.</summary>
public static class StatusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fulfilment business-events emitter (TDD §6.3): the Service Bus publisher when
    /// "ServiceBusConnection" is configured (production / sandbox), else an in-process publisher
    /// that applies events straight to the status mirror (dev host / tests). Requires AddStatusSync.
    /// </summary>
    public static IServiceCollection AddStatusEventPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StatusOutboundOptions>(configuration.GetSection(StatusOutboundOptions.SectionName));

        var connection = configuration["ServiceBusConnection"];
        if (!string.IsNullOrWhiteSpace(connection))
        {
            services.AddSingleton(_ => new ServiceBusClient(connection));
            services.AddSingleton<IStatusEventPublisher, ServiceBusStatusEventPublisher>();
        }
        else
        {
            services.AddSingleton<IStatusEventPublisher, InProcessStatusEventPublisher>();
        }

        return services;
    }

    /// <summary>
    /// Durable order-status store over Redis when configured (DR-011) so the status-sync Function
    /// app and the storefront share one view; in-memory otherwise.
    /// </summary>
    public static IServiceCollection AddStatusSync(this IServiceCollection services, IConfiguration configuration)
    {
        if (DistributedCacheBackend.IsRedisConfigured(configuration))
        {
            services.AddPortalDistributedCache(configuration);
            services.AddSingleton<IOrderStatusStore, DistributedOrderStatusStore>();
        }
        else
        {
            services.AddSingleton<IOrderStatusStore, InMemoryOrderStatusStore>();
        }

        services.AddSingleton<IStatusSyncService, StatusSyncService>();
        return services;
    }
}
