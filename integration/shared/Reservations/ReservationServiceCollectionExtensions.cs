using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Shared.Ivs;

namespace PartsPortal.Shared.Reservations;

/// <summary>
/// Registers the reservation registry and the TTL release job. The caller must also register
/// the resilient HttpClients via AddExternalHttpClients (the "ivs" client).
/// </summary>
public static class ReservationServiceCollectionExtensions
{
    /// <summary>Registers the shared reservation registry (used by reserve/release/writeback flows).</summary>
    public static IServiceCollection AddReservationRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IReservationRegistry, InMemoryReservationRegistry>();
        return services;
    }

    /// <summary>Registers the registry + IVS client + TTL release service (for the ReservationRelease job).</summary>
    public static IServiceCollection AddReservationRelease(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IvsOptions>(configuration.GetSection(IvsOptions.SectionName));
        services.AddReservationRegistry();
        services.AddSingleton<IIvsClient, IvsClient>();
        services.AddSingleton<ReservationReleaseService>();
        return services;
    }
}
