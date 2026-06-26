namespace PartsPortal.Shared.Status;

/// <summary>
/// The storefront order-status mirror. Fulfilments accumulate across events (a single order can
/// ship in multiple shipments). Phase 1 is in-memory; Phase 2 swaps a durable store behind this
/// interface (and the storefront/BFF reads it for order detail / `/order/{id}/status`).
/// </summary>
public interface IOrderStatusStore
{
    OrderStatusView? Get(string salesOrderNumber);

    /// <summary>Appends <paramref name="newFulfilments"/> and updates status / remaining backorder.</summary>
    void Apply(string salesOrderNumber, StorefrontOrderStatus status, IReadOnlyList<Fulfilment> newFulfilments, decimal? remainingBackorder);
}
