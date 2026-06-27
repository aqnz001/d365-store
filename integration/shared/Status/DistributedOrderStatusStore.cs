using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PartsPortal.Shared.Status;

/// <summary>
/// Durable order-status mirror over <see cref="IDistributedCache"/> (Phase 2, DR-011): keyed by
/// sales order number so the BFF/storefront and the status-sync Function app share one view.
/// <see cref="Apply"/> is read-modify-write (accumulate fulfilments); under Redis the per-order
/// Service Bus session (status events for one order are serialized) keeps that race narrow.
/// </summary>
public sealed class DistributedOrderStatusStore(IDistributedCache cache) : IOrderStatusStore
{
    private const string Prefix = "status:";

    public OrderStatusView? Get(string salesOrderNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salesOrderNumber);
        var json = cache.GetString(Prefix + salesOrderNumber);
        return json is null ? null : JsonSerializer.Deserialize<OrderStatusView>(json);
    }

    public void Apply(string salesOrderNumber, StorefrontOrderStatus status, IReadOnlyList<Fulfilment> newFulfilments, decimal? remainingBackorder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salesOrderNumber);

        var existing = Get(salesOrderNumber);
        var fulfilments = existing is null
            ? newFulfilments.ToList()
            : existing.Fulfilments.Concat(newFulfilments).ToList();

        var view = new OrderStatusView(
            salesOrderNumber,
            status,
            fulfilments,
            remainingBackorder ?? existing?.RemainingBackorder);

        cache.SetString(Prefix + salesOrderNumber, JsonSerializer.Serialize(view));
    }
}
