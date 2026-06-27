using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Shared.Idempotency;
using PartsPortal.Shared.Ivs;
using PartsPortal.Shared.Observability;
using PartsPortal.Shared.Reservations;

namespace PartsPortal.Shared.Writeback;

/// <summary>
/// Registers the order-writeback stack. The caller must also register the resilient
/// HttpClients via AddExternalHttpClients ("odata" + "ivs" clients).
/// </summary>
public static class WritebackServiceCollectionExtensions
{
    private const string PriceIntegritySectionName = "PriceIntegrity";

    public static IServiceCollection AddWriteback(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IvsOptions>(configuration.GetSection(IvsOptions.SectionName));

        // De-dup store: durable Redis when "Redis:ConnectionString" is set (Phase 2), else in-memory.
        services.AddIdempotencyStore(configuration);
        services.AddSingleton<IODataOrderClient, ODataOrderClient>();
        services.AddSingleton<IIvsClient, IvsClient>();
        services.AddReservationRegistry(configuration);
        services.AddPortalMetrics();

        // Price-integrity check (TDD §9) is enabled only when a tolerance is configured; otherwise
        // the locked price is authoritative and OrderWritebackService skips the comparison.
        var priceSection = configuration.GetSection(PriceIntegritySectionName);
        if (priceSection.Exists())
        {
            var options = new PriceIntegrityOptions();
            priceSection.Bind(options);
            services.AddSingleton(options);
            services.AddSingleton<PriceIntegrityPolicy>();
        }

        services.AddSingleton<OrderWritebackService>();
        return services;
    }
}
