using Microsoft.Extensions.Logging;
using PartsPortal.Shared.Contracts.Messages;
using PartsPortal.Shared.Notifications;

namespace PartsPortal.Shared.Status;

/// <summary>
/// Maps FinOps fulfilment status events (pack/ship/invoice/statusChanged) to the storefront
/// order-status mirror (TDD §6.3). One order may emit multiple shipments → multiple fulfilments,
/// each with its own tracking; remaining backorder is reflected. On a shipment it also sends the
/// customer a tracking email (#7) — the recipient is resolved from the customer master, never the
/// event (no PII in messages).
/// </summary>
public interface IStatusSyncService
{
    Task ApplyAsync(FulfilmentStatusEvent statusEvent, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class StatusSyncService(
    IOrderStatusStore store,
    INotificationContacts contacts,
    IEmailSender emailSender,
    ILogger<StatusSyncService> logger) : IStatusSyncService
{
    public async Task ApplyAsync(FulfilmentStatusEvent statusEvent, CancellationToken ct = default)
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

        await SendShipmentNotificationAsync(statusEvent, status, fulfilments, ct);
    }

    // Tracking email on a (partial) shipment. Resolves the recipient from the customer master by the
    // non-PII customer account; a failure never fails the status sync.
    private async Task SendShipmentNotificationAsync(
        FulfilmentStatusEvent statusEvent,
        StorefrontOrderStatus status,
        IReadOnlyList<Fulfilment> fulfilments,
        CancellationToken ct)
    {
        if (status is not (StorefrontOrderStatus.Shipped or StorefrontOrderStatus.PartiallyShipped))
        {
            return;
        }

        var recipient = contacts.ResolveEmail(statusEvent.CustomerAccount ?? string.Empty);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        var tracking = fulfilments
            .Select(f => f.TrackingNumber)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        try
        {
            await emailSender.SendAsync(EmailTemplates.ShipmentDispatched(recipient, statusEvent.SalesOrderNumber, tracking), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shipment email failed for {SalesOrderNumber} (the status was still applied).", statusEvent.SalesOrderNumber);
        }
    }

    /// <summary>Maps an event type to the storefront status; a partial ship (backorder &gt; 0) is PartiallyShipped.</summary>
    public static StorefrontOrderStatus MapStatus(FulfilmentStatusEventEventType eventType, decimal? remainingBackorder) => eventType switch
    {
        FulfilmentStatusEventEventType.Packed => StorefrontOrderStatus.Packed,
        FulfilmentStatusEventEventType.Shipped => remainingBackorder is > 0 ? StorefrontOrderStatus.PartiallyShipped : StorefrontOrderStatus.Shipped,
        FulfilmentStatusEventEventType.Invoiced => StorefrontOrderStatus.Invoiced,
        FulfilmentStatusEventEventType.Returned => StorefrontOrderStatus.Returned,
        FulfilmentStatusEventEventType.Cancelled => StorefrontOrderStatus.Cancelled,
        _ => StorefrontOrderStatus.Updated,
    };
}
