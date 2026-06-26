namespace PartsPortal.Shared.Status;

/// <summary>Storefront-facing order status mirrored from FinOps fulfilment events (TDD §6.3).</summary>
public enum StorefrontOrderStatus
{
    Packed,
    Shipped,
    PartiallyShipped,
    Invoiced,
    Updated,
}

/// <summary>One line within a shipment/fulfilment.</summary>
public sealed record FulfilmentLine(string ItemNumber, decimal Quantity);

/// <summary>A shipment mirrored as a storefront fulfilment, with its own tracking number.</summary>
public sealed record Fulfilment(string TrackingNumber, IReadOnlyList<FulfilmentLine> Lines);

/// <summary>
/// The storefront mirror of an order's fulfilment status: accumulated fulfilments (one order
/// may have multiple shipments, each with its own tracking) and any remaining backorder.
/// </summary>
public sealed record OrderStatusView(
    string SalesOrderNumber,
    StorefrontOrderStatus Status,
    IReadOnlyList<Fulfilment> Fulfilments,
    decimal? RemainingBackorder);
