using Microsoft.Extensions.Caching.Distributed;

namespace PartsPortal.Shared.Idempotency;

/// <summary>
/// Durable idempotency store over <see cref="IDistributedCache"/> (Phase 2): backed by Redis in
/// production so de-dup holds across Function-app instances (replaces the in-memory store —
/// DR-011). First-write-wins via check-then-set; a Redis-native SETNX would make it atomic, but
/// the duplicate-message window is already narrow (Service Bus sessions serialize per customer).
/// </summary>
public sealed class DistributedIdempotencyStore(IDistributedCache cache) : IIdempotencyStore
{
    private const string Prefix = "idem:";

    public Task<string?> TryGetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return cache.GetStringAsync(Prefix + key, ct);
    }

    public async Task SetAsync(string key, string result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(result);

        // First-write-wins: never overwrite an existing terminal result.
        if (await cache.GetStringAsync(Prefix + key, ct) is null)
        {
            await cache.SetStringAsync(Prefix + key, result, ct);
        }
    }
}
