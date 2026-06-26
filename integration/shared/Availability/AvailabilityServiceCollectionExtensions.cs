using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Mapping;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Reservations;

namespace PartsPortal.Shared.Availability;

/// <summary>
/// Registers the availability stack (IVS client, band calculator, cart service). The caller
/// must also register the resilient HttpClients via AddExternalHttpClients so the "ivs"
/// client is available.
/// </summary>
public static class AvailabilityServiceCollectionExtensions
{
    public const string AvailabilitySectionName = "Availability";

    public static IServiceCollection AddAvailability(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IvsOptions>(configuration.GetSection(IvsOptions.SectionName));
        services.Configure<AvailabilityOptions>(configuration.GetSection(AvailabilitySectionName));

        services.AddSingleton(sp => new AvailabilityBandCalculator(
            sp.GetRequiredService<IOptions<AvailabilityOptions>>().Value));
        services.AddSingleton<IIvsClient, IvsClient>();
        services.AddReservationRegistry();
        services.AddPortalMetrics();
        services.AddSingleton<ICartAvailabilityService, CartAvailabilityService>();
        return services;
    }
}
