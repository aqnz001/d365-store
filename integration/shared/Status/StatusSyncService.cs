using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Messages;

namespace PartsPortal.Shared.Status;

/// <summary>
/// Maps FinOps fulfilment status events (pack/ship/invoice/statusChanged) to the storefront
/// order-status mirror (TDD §6.3). One order may emit multiple shipments → multiple fulfilments,
/// each with its own tracking; remaining backorder is reflected.
/// </summary>
public interface IStatusSyncService
{
    void Apply(FulfilmentStatusEvent statusEvent);
}

/// <inheritdoc />
public sealed class StatusSyncService(IOrderStatusStore store, ILogger<StatusSyncService> logger) : IStatusSyncService
{
    public void Apply(FulfilmentStatusEvent statusEvent)
    {
        ArgumentNullException.ThrowIfNull(statusEvent);

        var fulfilments = (statusEvent.Shipments ?? [])
            .Select(s => new Fulfilment(
                s.TrackingNumber,
                s.Lines.Select(l => new FulfilmentLine(l.ItemNumber, (decimal)l.Quantity)).ToList()))
            .ToList();

        var remaining = statusEvent.RemainingBackorder is { } value ? (decimal)value : (decimal?)null;
        var status = MapStatus(statusEvent.EventType, remaining);

        store.Apply(statusEvent.SalesOrderNumber, status, fulfilments, remaining);
        logger.LogInformation("Status sync: {SalesOrderNumber} → {Status} (+{Count} fulfilment(s)).",
            statusEvent.SalesOrderNumber, status, fulfilments.Count);
    }

    /// <summary>Maps an event type to the storefront status; a partial ship (backorder &gt; 0) is PartiallyShipped.</summary>
    public static StorefrontOrderStatus MapStatus(FulfilmentStatusEventEventType eventType, decimal? remainingBackorder) => eventType switch
    {
        FulfilmentStatusEventEventType.Packed => StorefrontOrderStatus.Packed,
        FulfilmentStatusEventEventType.Shipped => remainingBackorder is > 0 ? StorefrontOrderStatus.PartiallyShipped : StorefrontOrderStatus.Shipped,
        FulfilmentStatusEventEventType.Invoiced => StorefrontOrderStatus.Invoiced,
        _ => StorefrontOrderStatus.Updated,
    };
}
