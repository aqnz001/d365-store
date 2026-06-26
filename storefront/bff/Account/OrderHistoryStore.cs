using System.Collections.Concurrent;

namespace PartsPortal.Bff.Account;

/// <summary>An order the customer placed (the storefront's own record; the canonical order lives in FinOps).</summary>
public sealed record PlacedOrder(string OrderReference, DateTimeOffset PlacedAtUtc);

/// <summary>
/// Per-customer order history for the storefront. Phase 1 in-memory; Phase 2 a durable store
/// (the storefront mirror; FinOps remains the system of record).
/// </summary>
public interface IOrderHistoryStore
{
    void Record(string customerAccount, PlacedOrder order);

    IReadOnlyList<PlacedOrder> GetOrders(string customerAccount);
}

/// <inheritdoc />
public sealed class InMemoryOrderHistoryStore : IOrderHistoryStore
{
    private readonly ConcurrentDictionary<string, List<PlacedOrder>> _orders = new(StringComparer.Ordinal);

    public void Record(string customerAccount, PlacedOrder order) =>
        _orders.GetOrAdd(customerAccount, _ => []).Add(order);

    public IReadOnlyList<PlacedOrder> GetOrders(string customerAccount) =>
        _orders.TryGetValue(customerAccount, out var orders) ? orders.ToList() : [];
}
