using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Shared.Caching;

/// <summary>
/// One place to decide the durable-store backend (Phase 2, DR-011): Redis when
/// <c>Redis:ConnectionString</c> is configured, otherwise an in-memory <see cref="IDistributedCache"/>.
/// All the durable stores (idempotency, cart, order history, order status, reservation registry)
/// register the backend through here so they share a single <see cref="IDistributedCache"/>.
/// </summary>
public static class DistributedCacheBackend
{
    /// <summary>True when a Redis connection string is configured (i.e. run durable, not in-memory).</summary>
    public static bool IsRedisConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"]);

    /// <summary>
    /// Registers a single <see cref="IDistributedCache"/> backend (idempotent): Redis when
    /// configured, else in-memory. Safe to call from each store's registration extension.
    /// </summary>
    public static IServiceCollection AddPortalDistributedCache(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDistributedCache)))
        {
            return services; // backend already registered (another store got here first)
        }

        var redisConnection = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
