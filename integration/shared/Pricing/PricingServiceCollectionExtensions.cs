using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Shared.Pricing;

/// <summary>
/// Registers the pricing/credit stack. The caller must also register the resilient
/// HttpClients via AddExternalHttpClients so the "pricing-credit" client is available.
/// </summary>
public static class PricingServiceCollectionExtensions
{
    public static IServiceCollection AddPricingCredit(this IServiceCollection services)
    {
        services.AddSingleton<IPricingCreditClient, PricingCreditClient>();
        services.AddSingleton<IPricingCreditService, PricingCreditService>();
        return services;
    }
}
