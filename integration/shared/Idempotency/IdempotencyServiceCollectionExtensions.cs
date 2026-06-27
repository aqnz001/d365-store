using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PartsPortal.Shared.Idempotency;

/// <summary>Registers the idempotency store: durable Redis when configured, else in-memory.</summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// When "Redis:ConnectionString" is set, registers a Redis distributed cache + the durable
    /// <see cref="DistributedIdempotencyStore"/> (Phase 2, DR-011); otherwise the in-memory store
    /// (Phase 1 / dev / test). Same <see cref="IIdempotencyStore"/> interface either way.
    /// </summary>
    public static IServiceCollection AddIdempotencyStore(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
            services.AddSingleton<IIdempotencyStore, DistributedIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        }

        return services;
    }
}
