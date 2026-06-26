using System.Collections.Concurrent;

namespace PartsPortal.Shared.Status;

/// <summary>In-memory order-status mirror for Phase 1 (per-process). Phase 2 swaps a durable store.</summary>
public sealed class InMemoryOrderStatusStore : IOrderStatusStore
{
    private readonly ConcurrentDictionary<string, OrderStatusView> _orders = new(StringComparer.Ordinal);

    public OrderStatusView? Get(string salesOrderNumber) =>
        _orders.TryGetValue(salesOrderNumber, out var view) ? view : null;

    public void Apply(string salesOrderNumber, StorefrontOrderStatus status, IReadOnlyList<Fulfilment> newFulfilments, decimal? remainingBackorder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salesOrderNumber);

        _orders.AddOrUpdate(
            salesOrderNumber,
            _ => new OrderStatusView(salesOrderNumber, status, newFulfilments.ToList(), remainingBackorder),
            (_, existing) => existing with
            {
                Status = status,
                Fulfilments = existing.Fulfilments.Concat(newFulfilments).ToList(),
                RemainingBackorder = remainingBackorder ?? existing.RemainingBackorder,
            });
    }
}
