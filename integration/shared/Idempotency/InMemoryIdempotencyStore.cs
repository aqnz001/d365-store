using System.Collections.Concurrent;

namespace PartsPortal.Shared.Idempotency;

/// <summary>
/// In-memory <see cref="IIdempotencyStore"/> for Phase 1 (Golden Rule #6, TDD §8).
///
/// <para>
/// <b>First-write-wins semantics.</b> Once a key has recorded a result, <see cref="SetAsync"/>
/// will NOT overwrite it. The de-dup gate exists so that a duplicate writeback returns the
/// <em>original</em> sales-order number; a late/racing duplicate must never clobber that value
/// with a second (different) order number. The first writer to commit a result owns the key.
/// </para>
///
/// <para>
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>:
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> is atomic, so concurrent writers for the
/// same key resolve to a single stored result regardless of interleaving.
/// </para>
///
/// <para>
/// TODO(T4): Phase 2 swaps this for a durable store (Redis / Azure Table) behind the same
/// <see cref="IIdempotencyStore"/> interface — no caller change. This implementation is process-local
/// and non-persistent, so it is suitable only for the mock/contract phase and single-instance tests.
/// </para>
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    // Ordinal comparison: idempotency keys are opaque tokens (GUIDs / correlation ids),
    // never culture-sensitive text.
    private readonly ConcurrentDictionary<string, string> _store =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the previously-recorded result for <paramref name="key"/>, or <c>null</c> if unseen.
    /// </summary>
    public Task<string?> TryGetAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_store.TryGetValue(key, out var result) ? result : null);
    }

    /// <summary>
    /// Records the terminal result (e.g. sales-order number) for <paramref name="key"/>.
    /// First-write-wins: if the key already has a result, the existing value is kept and this call
    /// is a no-op, so a duplicate can never overwrite the original sales-order number.
    /// </summary>
    public Task SetAsync(string key, string result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);
        ct.ThrowIfCancellationRequested();

        // TryAdd is atomic and only succeeds on the first write for a key.
        _store.TryAdd(key, result);
        return Task.CompletedTask;
    }
}
