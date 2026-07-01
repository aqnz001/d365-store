using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var fullyQualifiedNamespace = configuration["ServiceBusConnection:fullyQualifiedNamespace"];
        if (!string.IsNullOrWhiteSpace(connection))
        {
            services.TryAddSingleton(_ => new ServiceBusClient(connection));
            services.AddSingleton<IStatusEventPublisher, ServiceBusStatusEventPublisher>();
        }
        else if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
        {
            services.TryAddSingleton(_ => new ServiceBusClient(fullyQualifiedNamespace, new Azure.Identity.DefaultAzureCredential()));
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

        // Shipment tracking emails (#7): the recipient is resolved from the customer master (config
        // stand-in Phase 1), the message never carries it. Logging email sender for Phase 1.
        services.TryAddSingleton<Notifications.INotificationContacts, Notifications.ConfigNotificationContacts>();
        services.TryAddSingleton<Notifications.IEmailSender, Notifications.LoggingEmailSender>();

        services.AddSingleton<IStatusSyncService, StatusSyncService>();
        return services;
    }
}
