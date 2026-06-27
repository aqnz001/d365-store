using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PartsPortal.Bff.Account;

/// <summary>
/// Durable per-customer order history over <see cref="IDistributedCache"/> (Phase 2, DR-011):
/// the storefront's own mirror of placed orders, surviving BFF restarts / scale-out (FinOps
/// remains the system of record).
/// </summary>
public sealed class DistributedOrderHistoryStore(IDistributedCache cache) : IOrderHistoryStore
{
    private const string Prefix = "orders:";

    public void Record(string customerAccount, PlacedOrder order)
    {
        var orders = GetOrders(customerAccount).ToList();
        orders.Add(order);
        cache.SetString(Prefix + customerAccount, JsonSerializer.Serialize(orders));
    }

    public IReadOnlyList<PlacedOrder> GetOrders(string customerAccount)
    {
        var json = cache.GetString(Prefix + customerAccount);
        return json is null ? [] : JsonSerializer.Deserialize<List<PlacedOrder>>(json) ?? [];
    }
}
