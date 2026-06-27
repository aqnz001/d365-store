using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Shared.Caching;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Observability;

namespace PartsPortal.Shared.Reservations;

/// <summary>
/// Registers the reservation registry and the TTL release job. The caller must also register
/// the resilient HttpClients via AddExternalHttpClients (the "ivs" client).
/// </summary>
public static class ReservationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared reservation registry. Durable over Redis when configured (DR-011) so
    /// the availability app and the separate TTL-release job share reservations; in-memory otherwise.
    /// </summary>
    public static IServiceCollection AddReservationRegistry(this IServiceCollection services, IConfiguration configuration)
    {
        if (DistributedCacheBackend.IsRedisConfigured(configuration))
        {
            services.AddPortalDistributedCache(configuration);
            services.AddSingleton<IReservationRegistry, DistributedReservationRegistry>();
        }
        else
        {
            services.AddSingleton<IReservationRegistry, InMemoryReservationRegistry>();
        }

        return services;
    }

    /// <summary>Registers the registry + IVS client + TTL release service (for the ReservationRelease job).</summary>
    public static IServiceCollection AddReservationRelease(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IvsOptions>(configuration.GetSection(IvsOptions.SectionName));
        services.AddReservationRegistry(configuration);
        services.AddSingleton<IIvsClient, IvsClient>();
        services.AddPortalMetrics();
        services.AddSingleton<ReservationReleaseService>();
        return services;
    }
}
