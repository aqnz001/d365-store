using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Shared.Status;

/// <summary>Registers the order-status mirror store + the status sync service.</summary>
public static class StatusServiceCollectionExtensions
{
    public static IServiceCollection AddStatusSync(this IServiceCollection services)
    {
        services.AddSingleton<IOrderStatusStore, InMemoryOrderStatusStore>();
        services.AddSingleton<IStatusSyncService, StatusSyncService>();
        return services;
    }
}
