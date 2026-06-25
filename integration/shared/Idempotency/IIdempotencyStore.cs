namespace PartsPortal.Shared.Idempotency;

/// <summary>
/// De-dup gate for reservations and order writeback (Golden Rule #6, TDD §8).
/// Writeback checks the store BEFORE create; a duplicate key returns the existing
/// sales-order number rather than creating a second order.
/// Implementation (table/Redis) lands in T4.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns the previously-recorded result for <paramref name="key"/>, or null if unseen.</summary>
    Task<string?> TryGetAsync(string key, CancellationToken ct = default);

    /// <summary>Records the terminal result (e.g. sales-order number) for <paramref name="key"/>.</summary>
    Task SetAsync(string key, string result, CancellationToken ct = default);
}
