using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PartsPortal.Shared.Observability;

/// <summary>Registers portal metrics (idempotent — safe to call from multiple stacks in one host).</summary>
public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddPortalMetrics(this IServiceCollection services)
    {
        services.TryAddSingleton<IPortalMetrics, DiagnosticsPortalMetrics>();
        return services;
    }
}
