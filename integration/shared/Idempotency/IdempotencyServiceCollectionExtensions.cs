using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PartsPortal.Shared.Caching;

namespace PartsPortal.Shared.Idempotency;

/// <summary>Registers the idempotency store: durable Redis when configured, else in-memory.</summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// When "Redis:ConnectionString" is set, registers the shared distributed-cache backend + the
    /// durable <see cref="DistributedIdempotencyStore"/> (Phase 2, DR-011); otherwise the in-memory
    /// store (Phase 1 / dev / test). Same <see cref="IIdempotencyStore"/> interface either way.
    /// </summary>
    public static IServiceCollection AddIdempotencyStore(this IServiceCollection services, IConfiguration configuration)
    {
        if (DistributedCacheBackend.IsRedisConfigured(configuration))
        {
            services.AddPortalDistributedCache(configuration);
            services.AddSingleton<IIdempotencyStore, DistributedIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        }

        return services;
    }
}
